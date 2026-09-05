using Fiscal.Application.Documentos;
using Fiscal.Domain.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class RepositorioDocumentos(FiscalDbContext db) : IRepositorioDocumentos
{
    public async Task<bool> TentarInserirAsync(DocumentoFiscal documento, CancellationToken cancellationToken)
    {
        db.Documentos.Add(documento);

        // Sem SELECT antes: o índice único decide, e não há janela entre verificar e
        // gravar. O savepoint dentro de Escrita.TentarAsync é o que permite fazer
        // isso dentro de uma transação maior sem abortá-la.
        return await Escrita.TentarAsync(
            db, "documento", [documento, .. documento.Itens], cancellationToken);
    }

    public Task<DocumentoFiscal?> ObterPorChaveAsync(
        TipoDocumentoFiscal tipo,
        string chaveAcesso,
        CancellationToken cancellationToken) =>
        SemFiltroDeExclusao()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                documento => documento.Tipo == tipo && documento.ChaveAcesso == chaveAcesso,
                cancellationToken);

    public async Task<PaginaDe<ResumoDocumento>> ListarAsync(
        FiltroDocumentos filtro,
        CancellationToken cancellationToken)
    {
        var consulta = db.Documentos.AsNoTracking().AsQueryable();

        if (filtro.DataInicio is { } inicio)
        {
            consulta = consulta.Where(documento => documento.DataEmissao >= inicio);
        }

        if (filtro.DataFim is { } fim)
        {
            consulta = consulta.Where(documento => documento.DataEmissao <= fim);
        }

        if (filtro.DocumentoDestinatario is { } destinatario)
        {
            consulta = consulta.Where(documento => documento.DocumentoDestinatario == destinatario);
        }

        if (filtro.Uf is { } uf)
        {
            consulta = consulta.Where(documento => documento.UfEmitente == uf);
        }

        // COUNT(*) numa segunda ida ao banco, com custo próximo ao da própria página.
        // É o preço de mostrar "página 3 de 47"; a alternativa (buscar tamanho+1 e só
        // informar se há próxima) está registrada como melhoria no README.
        var total = await consulta.CountAsync(cancellationToken);

        var itens = await consulta
            // Mesma ordem do índice ix_documento_fiscal_cnpj_data, para o Postgres
            // percorrer o índice em vez de ordenar em memória.
            .OrderByDescending(documento => documento.DataEmissao)
            .ThenBy(documento => documento.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamanho)
            .Take(filtro.Tamanho)
            // Projeção explícita: a listagem nunca carrega os itens do documento.
            .Select(documento => new ResumoDocumento(
                documento.Id,
                documento.Tipo.ToString(),
                documento.ChaveAcesso,
                documento.Numero,
                documento.Serie,
                documento.CnpjEmitente,
                documento.NomeEmitente,
                documento.UfEmitente,
                documento.DocumentoDestinatario,
                documento.NomeDestinatario,
                documento.DataEmissao,
                documento.ValorTotal,
                documento.Observacao))
            .ToListAsync(cancellationToken);

        return new PaginaDe<ResumoDocumento>(itens, filtro.Pagina, filtro.Tamanho, total);
    }

    public Task<DocumentoFiscal?> ObterParaLeituraAsync(Guid id, CancellationToken cancellationToken) =>
        SemFiltroDeExclusao()
            .AsNoTracking()
            .Include(documento => documento.Itens)
            .FirstOrDefaultAsync(documento => documento.Id == id, cancellationToken);

    public Task<DocumentoFiscal?> ObterParaEdicaoAsync(Guid id, CancellationToken cancellationToken) =>
        SemFiltroDeExclusao()
            .FirstOrDefaultAsync(documento => documento.Id == id, cancellationToken);

    public Task SalvarAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Desliga apenas o filtro de exclusão lógica, nominalmente. O isolamento por
    /// CNPJ continua ativo — é por isso que os filtros são nomeados: um
    /// <c>IgnoreQueryFilters()</c> sem argumento derrubaria os dois.
    /// </summary>
    private IQueryable<DocumentoFiscal> SemFiltroDeExclusao() =>
        db.Documentos.IgnoreQueryFilters([FiltrosDeConsulta.ExclusaoLogica]);
}
