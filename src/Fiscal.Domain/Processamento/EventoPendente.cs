namespace Fiscal.Domain.Processamento;

/// <summary>
/// Registro de outbox. É o espelho do inbox, do lado de quem publica.
/// <para>
/// Gravar no storage e publicar na fila são duas operações que não compartilham
/// transação: se a API cair entre elas, o item ficaria pendente para sempre e o
/// cliente veria um lote que nunca termina. Escrevendo a intenção de publicar na
/// MESMA transação que grava o lote, o commit passa a ser o único ponto de decisão —
/// ou tudo existe, ou nada existe. Um relay publica depois, e reentrega é problema
/// do inbox no outro lado.
/// </para>
/// </summary>
public sealed class EventoPendente
{
    private EventoPendente()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Identidade da mensagem, propagada no MessageId da AMQP.</summary>
    public string MensagemId { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset? PublicadoEm { get; private set; }

    public int Tentativas { get; private set; }

    public static EventoPendente Criar(string mensagemId, string payload, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(),
        MensagemId = mensagemId,
        Payload = payload,
        CriadoEm = agora,
    };

    public void MarcarPublicado(DateTimeOffset agora) => PublicadoEm = agora;

    public void RegistrarTentativa() => Tentativas++;
}
