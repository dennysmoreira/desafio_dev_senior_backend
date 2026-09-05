using Fiscal.Application.Mensageria;
using Microsoft.Extensions.Logging;

namespace Fiscal.Infrastructure.Mensageria;

/// <summary>
/// Publicador que só registra em log, para a API subir sem broker. Nesse modo o
/// outbox acumula e nada é ingerido — útil para testar o caminho síncrono, inútil
/// em produção, e o start avisa.
/// </summary>
public sealed class PublicadorEmLog(ILogger<PublicadorEmLog> logger) : IPublicadorEventos
{
    public Task PublicarAsync(string mensagemId, string payload, CancellationToken cancellationToken)
    {
        logger.LogInformation("Evento {MensagemId} (não publicado: sem broker configurado).", mensagemId);

        return Task.CompletedTask;
    }
}
