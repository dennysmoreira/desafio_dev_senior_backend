namespace Fiscal.Application.Mensageria;

/// <summary>
/// Publicação de eventos de domínio. A implementação com RabbitMQ mora na
/// infraestrutura; nenhuma outra camada conhece o broker.
/// </summary>
public interface IPublicadorEventos
{
    Task PublicarAsync(DocumentoProcessado evento, CancellationToken cancellationToken);
}

/// <summary>
/// Evento emitido a cada documento efetivamente gravado. Reenvio idempotente NÃO
/// republica: o efeito colateral já aconteceu na primeira vez.
/// <para>
/// O <paramref name="MensagemId"/> é o hash do conteúdo, não um GUID novo a cada
/// publicação — assim a mesma ingestão sempre produz a mesma identidade de
/// mensagem, e o inbox do consumidor consegue reconhecer a reentrega.
/// </para>
/// </summary>
public sealed record DocumentoProcessado(
    string MensagemId,
    Guid DocumentoId,
    string ChaveAcesso,
    string CnpjEmitente,
    DateTimeOffset DataEmissao,
    decimal ValorTotal);
