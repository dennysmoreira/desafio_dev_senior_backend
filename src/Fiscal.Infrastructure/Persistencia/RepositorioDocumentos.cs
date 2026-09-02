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
        db.Documentos
            // Ignora SÓ o filtro de exclusão lógica, nunca o de isolamento por CNPJ.
            // Um documento excluído continua ocupando sua chave no índice único, então
            // sem isto o reenvio do XML de um documento excluído cairia num estado
            // impossível: o INSERT falha e a consulta não acha nada.
            .IgnoreQueryFilters([FiltrosDeConsulta.ExclusaoLogica])
            .AsNoTracking()
            .FirstOrDefaultAsync(
                documento => documento.Tipo == tipo && documento.ChaveAcesso == chaveAcesso,
                cancellationToken);
}
