using Fiscal.Domain.Lotes;

namespace Fiscal.Application.Lotes;

public interface IRepositorioLotes
{
    /// <summary>
    /// Grava o lote, seus itens e os eventos de outbox numa única transação. Ou tudo
    /// existe, ou nada existe — não há estado em que o lote foi aceito mas ninguém
    /// vai processá-lo.
    /// </summary>
    Task GravarComEventosAsync(
        LoteDeIngestao lote,
        IReadOnlyList<(string MensagemId, string Payload)> eventos,
        DateTimeOffset agora,
        CancellationToken cancellationToken);

    Task<LoteDeIngestao?> ObterAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoteDeIngestao>> ListarRecentesAsync(int quantidade, CancellationToken cancellationToken);

    /// <summary>Com rastreamento e com os itens, para o worker avançar o estado.</summary>
    Task<LoteDeIngestao?> ObterParaEdicaoAsync(Guid id, CancellationToken cancellationToken);

    Task SalvarAsync(CancellationToken cancellationToken);
}
