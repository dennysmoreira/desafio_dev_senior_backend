namespace Fiscal.Domain.Comum;

/// <summary>
/// Violação de invariante do domínio. A API traduz para 400/422 — nunca para 500,
/// porque não é falha do servidor e não deve ser retentada pelo consumidor.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
