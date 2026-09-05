using Fiscal.Application.Processamento;
using Fiscal.Domain.Processamento;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class RepositorioProcessamento(FiscalDbContext db) : IRepositorioProcessamento
{
    public async Task<bool> TentarRegistrarAsync(
        string mensagemId,
        string consumidor,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        var registro = MensagemProcessada.Registrar(mensagemId, consumidor, agora);

        db.MensagensProcessadas.Add(registro);

        return await Escrita.TentarAsync(db, "inbox", [registro], cancellationToken);
    }
}
