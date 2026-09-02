using System.Text.Json;
using Fiscal.Application.Mensageria;
using RabbitMQ.Client;

namespace Fiscal.Infrastructure.Mensageria;

public sealed class PublicadorRabbitMq(ConexaoRabbitMq conexao) : IPublicadorEventos
{
    public async Task PublicarAsync(DocumentoProcessado evento, CancellationToken cancellationToken)
    {
        var ligacao = await conexao.ObterAsync(cancellationToken);

        await using var canal = await ligacao.CreateChannelAsync(cancellationToken: cancellationToken);

        await TopologiaRabbitMq.DeclararAsync(canal, cancellationToken);

        var propriedades = new BasicProperties
        {
            // A identidade da mensagem é o hash do XML, não um GUID novo a cada
            // publicação. É o que permite ao inbox do consumidor reconhecer uma
            // reentrega: mesma ingestão, sempre a mesma identidade.
            MessageId = evento.MensagemId,
            ContentType = "application/json",

            // Sobrevive a reinício do broker. Sem isto, a fila é perdida junto com
            // o processo e documentos processados nunca chegam ao resumo.
            Persistent = true,
        };

        await canal.BasicPublishAsync(
            TopologiaRabbitMq.Exchange,
            TopologiaRabbitMq.ChaveDeRoteamento,
            mandatory: false,
            basicProperties: propriedades,
            body: JsonSerializer.SerializeToUtf8Bytes(evento),
            cancellationToken: cancellationToken);
    }
}
