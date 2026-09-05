using System.Text.Json;
using Fiscal.Application.Armazenamento;
using Fiscal.Application.Seguranca;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Lotes;
using Microsoft.Extensions.Logging;

namespace Fiscal.Application.Lotes;

/// <summary>
/// Caminho síncrono da ingestão, e ele faz de propósito o mínimo possível: calcula o
/// hash, grava no storage e registra a intenção de processar. Nada de parse, nada de
/// validação fiscal, nada de escrever documento — isso é trabalho do worker.
/// <para>
/// A ordem importa. O arquivo vai para o storage ANTES da transação: se a transação
/// falhar, sobra um objeto órfão, que é inofensivo porque a chave é o hash do
/// conteúdo e o reenvio escreve por cima. O inverso — registrar antes de gravar —
/// deixaria o worker procurando um arquivo que não existe.
/// </para>
/// </summary>
public sealed class ReceberLote(
    IArmazenamentoDeXml armazenamento,
    IRepositorioLotes repositorio,
    IContextoAcesso contexto,
    TimeProvider relogio,
    ILogger<ReceberLote> logger)
{
    public async Task<LoteAceito> ExecutarAsync(
        IReadOnlyList<ArquivoParaIngestao> arquivos,
        CancellationToken cancellationToken)
    {
        if (arquivos.Count == 0)
        {
            throw new DomainException("Envie ao menos um arquivo.");
        }

        if (arquivos.Count > LoteDeIngestao.MaximoDeArquivos)
        {
            throw new DomainException(
                $"Lote aceita no máximo {LoteDeIngestao.MaximoDeArquivos} arquivos; vieram {arquivos.Count}.");
        }

        var agora = relogio.GetUtcNow();
        var lote = LoteDeIngestao.Registrar(contexto.CnpjAutorizado!, agora);

        var eventos = new List<(string, string)>(arquivos.Count);

        foreach (var arquivo in arquivos)
        {
            var chave = HashConteudo.Calcular(arquivo.Conteudo.Span);

            await armazenamento.GravarAsync(chave, arquivo.Conteudo, cancellationToken);

            var item = lote.Adicionar(arquivo.Nome, chave, arquivo.Conteudo.Length);

            var evento = new ArquivoRecebido(
                lote.Id, item.Id, chave, lote.CnpjProprietario, arquivo.Nome);

            // A identidade da mensagem é o id do item. Estável entre reentregas e
            // única por arquivo dentro do lote — é o que o inbox do worker usa.
            eventos.Add((item.Id.ToString(), JsonSerializer.Serialize(evento)));
        }

        await repositorio.GravarComEventosAsync(lote, eventos, agora, cancellationToken);

        logger.LogInformation(
            "Lote {LoteId} aceito com {Quantidade} arquivo(s).", lote.Id, arquivos.Count);

        return new LoteAceito(lote.Id, arquivos.Count);
    }
}

public sealed class ConsultarLote(IRepositorioLotes repositorio)
{
    public async Task<SituacaoDoLote?> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var lote = await repositorio.ObterAsync(id, cancellationToken);

        return lote is null ? null : SituacaoDoLote.De(lote);
    }
}

public sealed class ListarLotes(IRepositorioLotes repositorio)
{
    public async Task<IReadOnlyList<SituacaoDoLote>> ExecutarAsync(
        int quantidade,
        CancellationToken cancellationToken)
    {
        var lotes = await repositorio.ListarRecentesAsync(quantidade, cancellationToken);

        return [.. lotes.Select(SituacaoDoLote.De)];
    }
}
