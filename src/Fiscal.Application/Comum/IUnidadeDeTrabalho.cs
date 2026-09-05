namespace Fiscal.Application.Comum;

/// <summary>
/// Transação explícita, porque a ingestão de um arquivo toca cinco coisas que
/// precisam cair juntas ou não cair: o inbox da mensagem, o documento, a situação do
/// item, a situação do lote e o resumo do emitente.
/// <para>
/// A abstração existe para que a orquestração dessa transação viva no caso de uso —
/// onde a regra está — e não dentro de um método de repositório na infraestrutura.
/// </para>
/// </summary>
public interface IUnidadeDeTrabalho
{
    Task<T> ExecutarAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken cancellationToken);
}
