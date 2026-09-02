using Fiscal.Application.Seguranca;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;
using Microsoft.Extensions.Logging;

namespace Fiscal.Application.Documentos;

public enum ResultadoConsulta
{
    Encontrado,

    /// <summary>Não existe, ou existe sob outro CNPJ. A API responde 404 nos dois casos.</summary>
    NaoEncontrado,

    /// <summary>Existiu e foi excluído logicamente. A API responde 410.</summary>
    Excluido,
}

public sealed record RespostaConsulta(ResultadoConsulta Resultado, DocumentoFiscal? Documento);

/// <summary>
/// Detalhe de um documento. Diferente da listagem, devolve o CPF/CNPJ do
/// destinatário íntegro — consulta individual autorizada é uso legítimo — e por
/// isso registra o acesso: rastro de quem leu dado pessoal, e quando.
/// </summary>
public sealed class ObterDocumento(
    IRepositorioDocumentos repositorio,
    IContextoAcesso contexto,
    ILogger<ObterDocumento> logger)
{
    public async Task<RespostaConsulta> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var documento = await repositorio.ObterParaLeituraAsync(id, cancellationToken);

        if (documento is null)
        {
            // 404 também quando o documento pertence a outro CNPJ. Um 403 confirmaria
            // que o registro existe — vazamento de informação entre contribuintes.
            return new RespostaConsulta(ResultadoConsulta.NaoEncontrado, null);
        }

        if (documento.Excluido)
        {
            return new RespostaConsulta(ResultadoConsulta.Excluido, documento);
        }

        if (documento.DocumentoDestinatario is not null)
        {
            logger.LogInformation(
                "Acesso a dado pessoal: CNPJ {Cnpj} consultou o destinatário do documento {DocumentoId}.",
                DadosSensiveis.Mascarar(contexto.CnpjAutorizado),
                documento.Id);
        }

        return new RespostaConsulta(ResultadoConsulta.Encontrado, documento);
    }
}

public sealed class AtualizarObservacaoDocumento(IRepositorioDocumentos repositorio, TimeProvider relogio)
{
    public async Task<RespostaConsulta> ExecutarAsync(
        Guid id,
        string? observacao,
        CancellationToken cancellationToken)
    {
        var documento = await repositorio.ObterParaEdicaoAsync(id, cancellationToken);

        if (documento is null)
        {
            return new RespostaConsulta(ResultadoConsulta.NaoEncontrado, null);
        }

        if (documento.Excluido)
        {
            return new RespostaConsulta(ResultadoConsulta.Excluido, documento);
        }

        // O domínio só expõe este caminho de mutação. Chave, emitente, valores e
        // itens não têm setter — o PUT não alcança o documento fiscal, por construção.
        documento.AtualizarObservacao(observacao, relogio.GetUtcNow());

        await repositorio.SalvarAsync(cancellationToken);

        return new RespostaConsulta(ResultadoConsulta.Encontrado, documento);
    }
}

public sealed class ExcluirDocumento(IRepositorioDocumentos repositorio, TimeProvider relogio)
{
    /// <summary>
    /// Exclusão lógica. A linha permanece, e continua ocupando sua chave no índice
    /// único — reenviar o XML de um documento excluído não recria o registro.
    /// Excluir o que já está excluído é no-op, para o DELETE ser idempotente.
    /// </summary>
    public async Task<ResultadoConsulta> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var documento = await repositorio.ObterParaEdicaoAsync(id, cancellationToken);

        if (documento is null)
        {
            return ResultadoConsulta.NaoEncontrado;
        }

        documento.Excluir(relogio.GetUtcNow());

        await repositorio.SalvarAsync(cancellationToken);

        return ResultadoConsulta.Encontrado;
    }
}
