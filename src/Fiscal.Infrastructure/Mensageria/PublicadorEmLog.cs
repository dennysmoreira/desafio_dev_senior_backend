using Fiscal.Application.Mensageria;
using Microsoft.Extensions.Logging;

namespace Fiscal.Infrastructure.Mensageria;

/// <summary>
/// Publicador provisório, trocado pela implementação com RabbitMQ no bloco de
/// mensageria. Existe para o pipeline de ingestão fechar ponta a ponta antes de o
/// broker entrar — e para os testes de ingestão não precisarem de fila.
/// </summary>
public sealed class PublicadorEmLog(ILogger<PublicadorEmLog> logger) : IPublicadorEventos
{
    public Task PublicarAsync(DocumentoProcessado evento, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Evento DocumentoProcessado (mensagem {MensagemId}) para o documento {DocumentoId}.",
            evento.MensagemId,
            evento.DocumentoId);

        return Task.CompletedTask;
    }
}
