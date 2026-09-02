using Fiscal.Application.Documentos;
using Fiscal.Application.Mensageria;
using Fiscal.Application.Seguranca;
using Fiscal.Domain.Documentos;

namespace Fiscal.UnitTests.Dubles;

/// <summary>
/// Repositório em memória que imita a única coisa que importa para os testes de
/// ingestão: um índice único recusando chave repetida. Não é um banco de mentira —
/// é a regra de unicidade isolada.
/// </summary>
internal sealed class RepositorioFalso : IRepositorioDocumentos
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
        throw new NotSupportedException("Listagem é coberta pelos testes de integração, contra o banco real.");

    public Task<DocumentoFiscal?> ObterParaLeituraAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Documentos.FirstOrDefault(d => d.Id == id));

    public Task<DocumentoFiscal?> ObterParaEdicaoAsync(Guid id, CancellationToken cancellationToken) =>
        ObterParaLeituraAsync(id, cancellationToken);

    public Task SalvarAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class PublicadorFalso : IPublicadorEventos
{
    public List<DocumentoProcessado> Publicados { get; } = [];

    public Task PublicarAsync(DocumentoProcessado evento, CancellationToken cancellationToken)
    {
        Publicados.Add(evento);

        return Task.CompletedTask;
    }
}

internal sealed class ContextoFalso : IContextoAcesso
{
    public string? CnpjAutorizado { get; set; }
}
