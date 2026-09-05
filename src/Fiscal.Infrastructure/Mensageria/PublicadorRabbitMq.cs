using System.Text;
using Fiscal.Application.Mensageria;
using RabbitMQ.Client;

namespace Fiscal.Infrastructure.Mensageria;

public sealed class PublicadorRabbitMq(ConexaoRabbitMq conexao) : IPublicadorEventos
{
    public async Task PublicarAsync(string mensagemId, string payload, CancellationToken cancellationToken)
    {
        var ligacao = await conexao.ObterAsync(cancellationToken);

        await using var canal = await ligacao.CreateChannelAsync(cancellationToken: cancellationToken);

        await TopologiaRabbitMq.DeclararAsync(canal, cancellationToken);

        var propriedades = new BasicProperties
        {
            // Identidade estável da mensagem — é por ela que o inbox do worker
            // reconhece reentrega. Vem do outbox, não é gerada aqui.
            MessageId = mensagemId,
            ContentType = "application/json",

            // Sobrevive a reinício do broker. Sem isto, uma fila perdida com o
            // processo deixaria lotes inteiros presos em "Recebido" para sempre.
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
}
