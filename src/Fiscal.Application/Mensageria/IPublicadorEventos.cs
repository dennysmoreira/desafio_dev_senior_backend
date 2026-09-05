namespace Fiscal.Application.Mensageria;

/// <summary>
/// Publicação de eventos. Recebe o payload já serializado porque quem chama é o
/// relay do outbox, que leu do banco exatamente os bytes que devem ir para a fila —
/// reserializar abriria espaço para o que foi gravado e o que foi publicado
/// divergirem.
/// </summary>
public interface IPublicadorEventos
{
    Task PublicarAsync(string mensagemId, string payload, CancellationToken cancellationToken);
}
