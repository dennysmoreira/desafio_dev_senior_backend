using Fiscal.Application.Comum;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class UnidadeDeTrabalho(FiscalDbContext db) : IUnidadeDeTrabalho
{
    public async Task<T> ExecutarAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        var resultado = await operacao(cancellationToken);

        await transacao.CommitAsync(cancellationToken);

        return resultado;
    }
}
