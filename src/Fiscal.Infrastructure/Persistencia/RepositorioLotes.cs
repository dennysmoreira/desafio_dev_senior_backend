using Fiscal.Application.Lotes;
using Fiscal.Domain.Lotes;
using Fiscal.Domain.Processamento;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class RepositorioLotes(FiscalDbContext db) : IRepositorioLotes
{
    public async Task GravarComEventosAsync(
        LoteDeIngestao lote,
        IReadOnlyList<(string MensagemId, string Payload)> eventos,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        db.Lotes.Add(lote);

        foreach (var (mensagemId, payload) in eventos)
        {
            db.EventosPendentes.Add(EventoPendente.Criar(mensagemId, payload, agora));
        }

        // Uma única chamada, uma única transação implícita do SaveChanges. O lote e a
        // intenção de publicá-lo nascem juntos ou não nascem.
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<LoteDeIngestao?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        db.Lotes
            .AsNoTracking()
            .Include(l => l.Itens)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<LoteDeIngestao?> ObterParaEdicaoAsync(Guid id, CancellationToken cancellationToken) =>
        db.Lotes
            .Include(l => l.Itens)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task SalvarAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<LoteDeIngestao>> ListarRecentesAsync(
        int quantidade,
        CancellationToken cancellationToken) =>
        await db.Lotes
            .AsNoTracking()
            .Include(l => l.Itens)
            .OrderByDescending(l => l.RecebidoEm)
            .Take(quantidade)
            .ToListAsync(cancellationToken);
}
