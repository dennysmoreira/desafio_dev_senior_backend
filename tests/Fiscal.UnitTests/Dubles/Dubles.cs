using Fiscal.Application.Armazenamento;
using Fiscal.Application.Comum;
using Fiscal.Application.Documentos;
using Fiscal.Application.Lotes;
using Fiscal.Application.Mensageria;
using Fiscal.Application.Processamento;
using Fiscal.Application.Resumos;
using Fiscal.Application.Seguranca;
using Fiscal.Domain.Documentos;
using Fiscal.Domain.Lotes;

namespace Fiscal.UnitTests.Dubles;

/// <summary>
/// Repositório em memória que imita a única coisa que importa nestes testes: um
/// índice único recusando chave repetida. Não é um banco de mentira — é a regra de
/// unicidade isolada.
/// </summary>
internal sealed class RepositorioDocumentosFalso : IRepositorioDocumentos
{
    public List<DocumentoFiscal> Documentos { get; } = [];

    public Task<bool> TentarInserirAsync(DocumentoFiscal documento, CancellationToken cancellationToken)
    {
        var jaExiste = Documentos.Any(
            d => d.Tipo == documento.Tipo && d.ChaveAcesso == documento.ChaveAcesso);

        if (jaExiste)
        {
            return Task.FromResult(false);
        }

        Documentos.Add(documento);

        return Task.FromResult(true);
    }

    public Task<DocumentoFiscal?> ObterPorChaveAsync(
        TipoDocumentoFiscal tipo,
        string chaveAcesso,
        CancellationToken cancellationToken) =>
        Task.FromResult(Documentos.FirstOrDefault(d => d.Tipo == tipo && d.ChaveAcesso == chaveAcesso));

    public Task<PaginaDe<ResumoDocumento>> ListarAsync(
        FiltroDocumentos filtro,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Coberto pelos testes de integração, contra o banco real.");

    public Task<DocumentoFiscal?> ObterParaLeituraAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Documentos.FirstOrDefault(d => d.Id == id));

    public Task<DocumentoFiscal?> ObterParaEdicaoAsync(Guid id, CancellationToken cancellationToken) =>
        ObterParaLeituraAsync(id, cancellationToken);

    public Task SalvarAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ArmazenamentoFalso : IArmazenamentoDeXml
{
    private readonly Dictionary<string, byte[]> _objetos = [];

    public Task GravarAsync(string chave, ReadOnlyMemory<byte> conteudo, CancellationToken cancellationToken)
    {
        _objetos[chave] = conteudo.ToArray();

        return Task.CompletedTask;
    }

    public Task<byte[]?> LerAsync(string chave, CancellationToken cancellationToken) =>
        Task.FromResult(_objetos.GetValueOrDefault(chave));

    public int Quantidade => _objetos.Count;
}

internal sealed class RepositorioLotesFalso : IRepositorioLotes
{
    public List<LoteDeIngestao> Lotes { get; } = [];

    public List<(string MensagemId, string Payload)> Eventos { get; } = [];

    public Task GravarComEventosAsync(
        LoteDeIngestao lote,
        IReadOnlyList<(string MensagemId, string Payload)> eventos,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        Lotes.Add(lote);
        Eventos.AddRange(eventos);

        return Task.CompletedTask;
    }

    public Task<LoteDeIngestao?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Lotes.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LoteDeIngestao>> ListarRecentesAsync(
        int quantidade,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoteDeIngestao>>([.. Lotes.Take(quantidade)]);

    public Task<LoteDeIngestao?> ObterParaEdicaoAsync(Guid id, CancellationToken cancellationToken) =>
        ObterAsync(id, cancellationToken);

    public Task SalvarAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class RepositorioResumosFalso : IRepositorioResumos
{
    public List<(string Cnpj, string Competencia, decimal Valor)> Acumulos { get; } = [];

    public Task AcumularAsync(
        string cnpjEmitente,
        string competencia,
        decimal valorDocumento,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        Acumulos.Add((cnpjEmitente, competencia, valorDocumento));

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResumoDoEmitente>> ListarAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Inbox em memória: a segunda tentativa com a mesma chave devolve falso.</summary>
internal sealed class ProcessamentoFalso : IRepositorioProcessamento
{
    private readonly HashSet<string> _vistas = [];

    public Task<bool> TentarRegistrarAsync(
        string mensagemId,
        string consumidor,
        DateTimeOffset agora,
        CancellationToken cancellationToken) =>
        Task.FromResult(_vistas.Add($"{consumidor}|{mensagemId}"));
}

/// <summary>Sem transação de verdade: os dublês não têm o que reverter.</summary>
internal sealed class UnidadeDeTrabalhoFalsa : IUnidadeDeTrabalho
{
    public Task<T> ExecutarAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancellationToken) =>
        operacao(cancellationToken);
}

internal sealed class PublicadorFalso : IPublicadorEventos
{
    public List<(string MensagemId, string Payload)> Publicados { get; } = [];

    public Task PublicarAsync(string mensagemId, string payload, CancellationToken cancellationToken)
    {
        Publicados.Add((mensagemId, payload));

        return Task.CompletedTask;
    }
}

internal sealed class ContextoFalso : IContextoAcesso
{
    public string? CnpjAutorizado { get; set; }
}
