using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fiscal.Infrastructure.Persistencia;

/// <summary>
/// Executa uma escrita que pode esbarrar em índice único e devolve se ela passou.
/// <para>
/// O detalhe que obriga esta classe a existir: no PostgreSQL, <b>qualquer</b> erro
/// dentro de uma transação a deixa abortada — todo comando seguinte falha com
/// "current transaction is aborted" até um rollback. Como a idempotência aqui é
/// construída deixando o índice único falhar de propósito, sem savepoint a primeira
/// violação derrubaria a transação inteira da ingestão.
/// </para>
/// <para>
/// Com savepoint, a violação desfaz só a tentativa e a transação segue viva.
/// </para>
/// </summary>
internal static class Escrita
{
    private const string ViolacaoDeUnicidade = "23505";

    public static async Task<bool> TentarAsync(
        FiscalDbContext db,
        string nomeDoSavepoint,
        IEnumerable<object> paraDesanexarSeFalhar,
        CancellationToken cancellationToken)
    {
        var transacao = db.Database.CurrentTransaction;

        if (transacao is not null)
        {
            await transacao.CreateSavepointAsync(nomeDoSavepoint, cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException excecao)
            when (excecao.InnerException is PostgresException { SqlState: ViolacaoDeUnicidade })
        {
            if (transacao is not null)
            {
                await transacao.RollbackToSavepointAsync(nomeDoSavepoint, cancellationToken);
            }

            // O rastreador continuaria segurando a entidade recusada e tentaria
            // gravá-la de novo no próximo SaveChanges desta mesma transação.
            foreach (var entidade in paraDesanexarSeFalhar)
            {
                db.Entry(entidade).State = EntityState.Detached;
            }

            return false;
        }
    }
}
