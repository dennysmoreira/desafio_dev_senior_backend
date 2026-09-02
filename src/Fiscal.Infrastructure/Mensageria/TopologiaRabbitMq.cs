using RabbitMQ.Client;

namespace Fiscal.Infrastructure.Mensageria;

/// <summary>
/// Nomes e topologia das filas, num lugar só.
/// <para>
/// O desenho tem três caminhos, e a diferença entre eles é o item 8 do enunciado:
/// </para>
/// <list type="number">
///   <item>
///     <b>Caminho normal</b> — <c>fiscal.documentos</c> entrega em
///     <c>fiscal.documentos.resumo</c>.
///   </item>
///   <item>
///     <b>Falha transitória</b> (banco fora, timeout) — a mensagem vai para
///     <c>fiscal.documentos.retry</c>, uma fila sem consumidor com TTL. Quando o TTL
///     expira, o dead-letter da própria fila devolve a mensagem à principal. O
///     resultado é uma espera antes de tentar de novo, sem bloquear o canal nem
///     ocupar o consumidor num laço.
///   </item>
///   <item>
///     <b>Falha permanente</b> (XML inválido, layout desconhecido) ou tentativas
///     esgotadas — <c>fiscal.documentos.poison</c>, sem consumidor, para inspeção
///     humana. Retentar não adianta: nenhuma tentativa futura faz um XML malformado
///     virar válido.
///   </item>
/// </list>
/// </summary>
public static class TopologiaRabbitMq
{
    public const string Exchange = "fiscal.documentos";

    public const string ExchangeDeRetry = "fiscal.documentos.retry";

    public const string Fila = "fiscal.documentos.resumo";

    public const string FilaDeRetry = "fiscal.documentos.retry";

    public const string FilaVenenosa = "fiscal.documentos.poison";

    public const string ChaveDeRoteamento = "documento.processado";

    /// <summary>Espera antes de reentregar uma mensagem que falhou por causa transitória.</summary>
    public const int EsperaDeRetryEmMs = 10_000;

    /// <summary>
    /// Depois disto a mensagem vai para a fila venenosa. Sem teto, um erro
    /// transitório que virou permanente circula para sempre entre as duas filas.
    /// </summary>
    public const int MaximoDeTentativas = 3;

    public static async Task DeclararAsync(IChannel canal, CancellationToken cancellationToken)
    {
        await canal.ExchangeDeclareAsync(
            Exchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        await canal.ExchangeDeclareAsync(
            ExchangeDeRetry, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        // Fila principal: o que for rejeitado aqui cai no exchange de retry.
        await canal.QueueDeclareAsync(
            Fila, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = ExchangeDeRetry,
                ["x-dead-letter-routing-key"] = ChaveDeRoteamento,
            },
            cancellationToken: cancellationToken);

        // Fila de espera: ninguém consome. O TTL expira e o dead-letter devolve para
        // a exchange principal — é o backoff, implementado pelo próprio broker.
        await canal.QueueDeclareAsync(
            FilaDeRetry, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = EsperaDeRetryEmMs,
                ["x-dead-letter-exchange"] = Exchange,
                ["x-dead-letter-routing-key"] = ChaveDeRoteamento,
            },
            cancellationToken: cancellationToken);

        await canal.QueueDeclareAsync(
            FilaVenenosa, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);

        await canal.QueueBindAsync(Fila, Exchange, ChaveDeRoteamento, cancellationToken: cancellationToken);
        await canal.QueueBindAsync(
            FilaDeRetry, ExchangeDeRetry, ChaveDeRoteamento, cancellationToken: cancellationToken);
    }
}
