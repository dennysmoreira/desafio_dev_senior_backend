using Fiscal.Application.Mensageria;
using Fiscal.Application.Seguranca;
using Fiscal.Domain.Processamento;
using Fiscal.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fiscal.Infrastructure.Mensageria;

/// <summary>
/// Lê o outbox e publica na fila. Roda junto da API porque o outbox pertence a quem
/// escreve; mais réplicas de API significam mais escrita e mais capacidade de relay,
/// que é a proporção certa.
/// <para>
/// A seleção usa <c>FOR UPDATE SKIP LOCKED</c>: várias instâncias varrem a mesma
/// tabela ao mesmo tempo e cada linha é entregue a exatamente uma delas, sem que
/// nenhuma espere pela outra. Sem isso, duas réplicas publicariam a mesma mensagem —
/// o inbox do consumidor absorveria, mas gastando trabalho à toa.
/// </para>
/// <para>
/// Publicar e marcar como publicado não são atômicos entre broker e banco: se o
/// processo cair no meio, a mensagem sai duas vezes. É aceito de propósito — a
/// entrega é at-least-once por natureza e o inbox do worker já existe para isso.
/// Perder mensagem seria o problema grave; duplicar não é.
/// </para>
/// </summary>
public sealed class RelayDoOutbox(
    IServiceScopeFactory fabricaDeEscopos,
    ILogger<RelayDoOutbox> logger) : BackgroundService
{
    private const int TamanhoDoLote = 20;

    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Relay do outbox iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publicados = await PublicarPendentesAsync(stoppingToken);

                // Só dorme quando não havia nada. Com fila cheia, continua puxando.
                if (publicados == 0)
                {
                    await Task.Delay(Intervalo, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception excecao)
            {
                logger.LogError(excecao, "Falha na varredura do outbox; tentando de novo.");

                await Task.Delay(Intervalo, stoppingToken);
            }
        }
    }

    private async Task<int> PublicarPendentesAsync(CancellationToken cancellationToken)
    {
        using var escopo = fabricaDeEscopos.CreateScope();

        var definidor = escopo.ServiceProvider.GetRequiredService<IDefinidorContextoAcesso>();
        using var _ = definidor.AbrirEscopoDeSistema("relay do outbox");

        var db = escopo.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var publicador = escopo.ServiceProvider.GetRequiredService<IPublicadorEventos>();

        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        var pendentes = await db.EventosPendentes
            .FromSql($"""
                SELECT * FROM evento_pendente
                WHERE "PublicadoEm" IS NULL
                ORDER BY "CriadoEm"
                LIMIT {TamanhoDoLote}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (pendentes.Count == 0)
        {
            await transacao.RollbackAsync(cancellationToken);

            return 0;
        }

        foreach (var evento in pendentes)
        {
            evento.RegistrarTentativa();

            await publicador.PublicarAsync(evento.MensagemId, evento.Payload, cancellationToken);

            evento.MarcarPublicado(DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);

        logger.LogInformation("Relay publicou {Quantidade} evento(s).", pendentes.Count);

        return pendentes.Count;
    }
}
