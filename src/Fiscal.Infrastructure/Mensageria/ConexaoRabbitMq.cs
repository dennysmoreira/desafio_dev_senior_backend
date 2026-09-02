using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;

namespace Fiscal.Infrastructure.Mensageria;

/// <summary>
/// Conexão única e preguiçosa com o broker. Abrir conexão AMQP por requisição é
/// caro; o modelo correto é uma conexão por processo e um canal por uso.
/// <para>
/// A API sobe mesmo com o RabbitMQ fora do ar: a conexão só é tentada quando a
/// primeira mensagem precisa sair, e com retry exponencial. Falhar o start por
/// causa do broker transformaria uma indisponibilidade parcial em queda total.
/// </para>
/// </summary>
public sealed class ConexaoRabbitMq(OpcoesRabbitMq opcoes, ILogger<ConexaoRabbitMq> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _exclusao = new(1, 1);

    private readonly ResiliencePipeline _politica = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = argumentos =>
            {
                logger.LogWarning(
                    argumentos.Outcome.Exception,
                    "Falha ao conectar no RabbitMQ; tentativa {Tentativa}.",
                    argumentos.AttemptNumber + 1);

                return ValueTask.CompletedTask;
            },
        })
        .Build();

    private IConnection? _conexao;

    public async Task<IConnection> ObterAsync(CancellationToken cancellationToken)
    {
        if (_conexao is { IsOpen: true })
        {
            return _conexao;
        }

        await _exclusao.WaitAsync(cancellationToken);

        try
        {
            if (_conexao is { IsOpen: true })
            {
                return _conexao;
            }

            _conexao = await _politica.ExecuteAsync(
                async token =>
                {
                    var fabrica = new ConnectionFactory
                    {
                        Uri = new Uri(opcoes.Uri),
                        AutomaticRecoveryEnabled = true,
                        ClientProvidedName = "fiscal-api",
                    };

                    return await fabrica.CreateConnectionAsync(token);
                },
                cancellationToken);

            return _conexao;
        }
        finally
        {
            _exclusao.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
        }

        _exclusao.Dispose();
    }
}

public sealed class OpcoesRabbitMq
{
    public required string Uri { get; init; }

    /// <summary>
    /// Quantas mensagens o broker entrega antes de esperar confirmação. Sem limite,
    /// o consumidor puxa a fila inteira para a memória e perde tudo se cair.
    /// </summary>
    public ushort Prefetch { get; init; } = 10;
}
