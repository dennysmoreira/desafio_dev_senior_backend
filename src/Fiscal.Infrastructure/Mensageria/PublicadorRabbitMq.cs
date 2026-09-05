using System.Text;
using Fiscal.Application.Mensageria;
using RabbitMQ.Client;

namespace Fiscal.Infrastructure.Mensageria;

/// <summary>
/// Publicador com canal reaproveitado.
/// <para>
/// A primeira versão abria um canal AMQP por mensagem, e a conta apareceu na
/// medição: com o worker ocioso e milhares de itens pendentes, a fila ficava
/// praticamente vazia — o gargalo era abrir e fechar canal, não consumir. Canal é
/// caro de criar e feito para ser reaproveitado; conexão, mais ainda.
/// </para>
/// <para>
/// Canal do RabbitMQ.Client não é seguro para uso concorrente, então o acesso é
/// serializado por semáforo. Registrado como singleton para que o canal sobreviva
/// entre requisições.
/// </para>
/// </summary>
public sealed class PublicadorRabbitMq(ConexaoRabbitMq conexao) : IPublicadorEventos, IAsyncDisposable
{
    private readonly SemaphoreSlim _exclusao = new(1, 1);

    private IChannel? _canal;

    public async Task PublicarAsync(string mensagemId, string payload, CancellationToken cancellationToken)
    {
        await _exclusao.WaitAsync(cancellationToken);

        try
        {
            var canal = await ObterCanalAsync(cancellationToken);

            var propriedades = new BasicProperties
            {
                // Identidade estável da mensagem — é por ela que o inbox do worker
                // reconhece reentrega. Vem do outbox, não é gerada aqui.
                MessageId = mensagemId,
                ContentType = "application/json",

                // Sobrevive a reinício do broker. Sem isto, uma fila perdida com o
                // processo deixaria lotes presos em "Recebido" para sempre.
                Persistent = true,
            };

            await canal.BasicPublishAsync(
                TopologiaRabbitMq.Exchange,
                TopologiaRabbitMq.ChaveDeRoteamento,
                mandatory: false,
                basicProperties: propriedades,
                body: Encoding.UTF8.GetBytes(payload),
                cancellationToken: cancellationToken);
        }
        finally
        {
            _exclusao.Release();
        }
    }

    private async Task<IChannel> ObterCanalAsync(CancellationToken cancellationToken)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        // Recria depois de uma queda do broker. A topologia é declarada junto porque
        // declarar é idempotente e garante que a fila exista antes do primeiro envio.
        var ligacao = await conexao.ObterAsync(cancellationToken);

        _canal = await ligacao.CreateChannelAsync(cancellationToken: cancellationToken);

        await TopologiaRabbitMq.DeclararAsync(_canal, cancellationToken);

        return _canal;
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal is not null)
        {
            await _canal.DisposeAsync();
        }

        _exclusao.Dispose();
    }
}
