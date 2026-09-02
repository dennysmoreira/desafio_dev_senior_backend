namespace Fiscal.Domain.Processamento;

/// <summary>
/// Registro de inbox. O RabbitMQ entrega ao menos uma vez: a mesma mensagem pode
/// chegar duas vezes por reentrega, por nack ou por timeout de ack. O consumidor
/// grava esta linha na MESMA transação do efeito colateral; a chave única em
/// <see cref="MensagemId"/> faz a segunda tentativa falhar no banco, e não numa
/// verificação prévia que teria janela de corrida.
/// </summary>
public sealed class MensagemProcessada
{
    private MensagemProcessada()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Identidade da mensagem, propagada pelo produtor no MessageId da AMQP.</summary>
    public string MensagemId { get; private set; } = string.Empty;

    public string Consumidor { get; private set; } = string.Empty;

    public DateTimeOffset ProcessadaEm { get; private set; }

    public static MensagemProcessada Registrar(string mensagemId, string consumidor, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(),
        MensagemId = mensagemId,
        Consumidor = consumidor,
        ProcessadaEm = agora,
    };
}
