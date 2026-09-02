using System.Text.Json;
using Fiscal.Application.Mensageria;
using Fiscal.Application.Resumos;
using Fiscal.Application.Seguranca;
using Fiscal.Domain.Comum;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Fiscal.Infrastructure.Mensageria;

/// <summary>
/// Consome <see cref="DocumentoProcessado"/> e alimenta o resumo por emitente.
/// <para>
/// A resiliência tem dois níveis, e a diferença entre eles é o que o item 8 do
/// enunciado pede:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dentro do processo</b> — falha transitória (banco reiniciando, timeout de
///     rede) é retentada três vezes com backoff exponencial e jitter. A maioria das
///     falhas transitórias dura menos de um segundo e nunca chega a incomodar a fila.
///   </item>
///   <item>
///     <b>No broker</b> — se persistir, a mensagem é rejeitada e vai para a fila de
///     espera com TTL, voltando depois. Isso libera o canal em vez de segurar o
///     consumidor num laço, e sobrevive a reinício do processo.
///   </item>
/// </list>
/// <para>
/// Erro permanente não passa por nenhum dos dois: vai direto para a fila venenosa.
/// Retentar um XML malformado dez vezes só atrasa a fila e polui o log.
/// </para>
/// </summary>
public sealed class ConsumidorResumo(
    ConexaoRabbitMq conexao,
    OpcoesRabbitMq opcoes,
    IServiceScopeFactory fabricaDeEscopos,
    ILogger<ConsumidorResumo> logger) : BackgroundService
{
    private readonly ResiliencePipeline _politica = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,

            // DomainException fica de fora de propósito: é erro permanente e
            // retentar não muda o resultado.
            ShouldHandle = new PredicateBuilder().Handle<Exception>(excecao => excecao is not DomainException),
            OnRetry = argumentos =>
            {
                logger.LogWarning(
                    argumentos.Outcome.Exception,
                    "Falha transitória ao processar mensagem; tentativa {Tentativa}.",
                    argumentos.AttemptNumber + 1);

                return ValueTask.CompletedTask;
            },
        })
        .Build();

    private IChannel? _canal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ligacao = await conexao.ObterAsync(stoppingToken);

        _canal = await ligacao.CreateChannelAsync(cancellationToken: stoppingToken);

        await TopologiaRabbitMq.DeclararAsync(_canal, stoppingToken);
        await _canal.BasicQosAsync(0, opcoes.Prefetch, global: false, stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(_canal);
        consumidor.ReceivedAsync += TratarAsync;

        // autoAck falso: a mensagem só sai da fila depois que o efeito colateral
        // está gravado. Com autoAck, uma queda entre receber e persistir perderia
        // o documento silenciosamente.
        await _canal.BasicConsumeAsync(
            TopologiaRabbitMq.Fila, autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);

        logger.LogInformation("Consumidor de resumo escutando {Fila}.", TopologiaRabbitMq.Fila);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task TratarAsync(object remetente, BasicDeliverEventArgs entrega)
    {
        var canal = _canal!;

        DocumentoProcessado evento;

        try
        {
            evento = JsonSerializer.Deserialize<DocumentoProcessado>(entrega.Body.Span)
                ?? throw new DomainException("Mensagem vazia.");
        }
        catch (Exception excecao) when (excecao is JsonException or DomainException)
        {
            await DescartarAsync(canal, entrega, excecao, "mensagem ilegível");
            return;
        }

        try
        {
            await _politica.ExecuteAsync(
                async token => await ProcessarAsync(evento, token),
                CancellationToken.None);

            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false);
        }
        catch (DomainException excecao)
        {
            await DescartarAsync(canal, entrega, excecao, "erro permanente de domínio");
        }
        catch (Exception excecao)
        {
            if (ContarTentativas(entrega) >= TopologiaRabbitMq.MaximoDeTentativas)
            {
                await DescartarAsync(canal, entrega, excecao, "tentativas esgotadas");
                return;
            }

            logger.LogWarning(
                excecao,
                "Mensagem {MensagemId} devolvida para nova tentativa em {Espera}ms.",
                evento.MensagemId,
                TopologiaRabbitMq.EsperaDeRetryEmMs);

            // requeue falso manda para o dead-letter, que é a fila de espera com TTL.
            // Com requeue verdadeiro a mensagem voltaria para o início da fila
            // imediatamente e giraria em laço apertado, queimando CPU.
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task ProcessarAsync(DocumentoProcessado evento, CancellationToken cancellationToken)
    {
        using var escopo = fabricaDeEscopos.CreateScope();

        // O consumidor atende todos os contribuintes, então não há CNPJ autenticado.
        // O escopo de sistema é explícito e registra em log — é o rastro de auditoria
        // do acesso cross-tenant.
        var definidor = escopo.ServiceProvider.GetRequiredService<IDefinidorContextoAcesso>();
        using var _ = definidor.AbrirEscopoDeSistema($"consumidor {AtualizarResumoDoEmitente.NomeDoConsumidor}");

        var caso = escopo.ServiceProvider.GetRequiredService<AtualizarResumoDoEmitente>();

        await caso.ExecutarAsync(evento, cancellationToken);
    }

    /// <summary>Conta reentregas pelo cabeçalho x-death, mantido pelo próprio broker.</summary>
    private static long ContarTentativas(BasicDeliverEventArgs entrega)
    {
        if (entrega.BasicProperties.Headers?.TryGetValue("x-death", out var bruto) is not true
            || bruto is not List<object> mortes
            || mortes.Count == 0
            || mortes[0] is not Dictionary<string, object?> primeira
            || !primeira.TryGetValue("count", out var contagem))
        {
            return 0;
        }

        return contagem as long? ?? 0;
    }

    private async Task DescartarAsync(
        IChannel canal,
        BasicDeliverEventArgs entrega,
        Exception excecao,
        string motivo)
    {
        logger.LogError(
            excecao,
            "Mensagem {MensagemId} enviada para {Fila}: {Motivo}.",
            entrega.BasicProperties.MessageId,
            TopologiaRabbitMq.FilaVenenosa,
            motivo);

        await canal.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: TopologiaRabbitMq.FilaVenenosa,
            mandatory: false,
            basicProperties: new BasicProperties { MessageId = entrega.BasicProperties.MessageId, Persistent = true },
            body: entrega.Body.ToArray());

        // Confirma para tirar da fila principal: a cópia já está guardada para
        // inspeção humana e devolvê-la só ocuparia o consumidor de novo.
        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_canal is not null)
        {
            await _canal.CloseAsync(cancellationToken);
            await _canal.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
