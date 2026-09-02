using Fiscal.Application.Documentos;
using Fiscal.Domain.Documentos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class RepositorioDocumentos(FiscalDbContext db) : IRepositorioDocumentos
{
    /// <summary>unique_violation no catálogo de erros do PostgreSQL.</summary>
    private const string ViolacaoDeUnicidade = "23505";

    public async Task<bool> TentarInserirAsync(DocumentoFiscal documento, CancellationToken cancellationToken)
    {
        db.Documentos.Add(documento);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException excecao)
            when (excecao.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            // Perdemos a corrida para outra requisição que gravou a mesma chave, ou
            // é reenvio. Nos dois casos o banco decidiu — não houve janela entre
            // verificar e gravar, porque não houve verificação.
            //
            // Desanexar é obrigatório: o rastreador continuaria segurando a entidade
            // recusada e tentaria gravá-la de novo no próximo SaveChanges.
            db.Entry(documento).State = EntityState.Detached;

            foreach (var item in documento.Itens)
            {
                db.Entry(item).State = EntityState.Detached;
            }

            return false;
        }
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
