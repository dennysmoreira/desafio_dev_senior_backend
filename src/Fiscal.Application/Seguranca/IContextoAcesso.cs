namespace Fiscal.Application.Seguranca;

/// <summary>
/// Quem está fazendo a chamada. O isolamento por CNPJ é aplicado no
/// <c>DbContext</c> a partir daqui — não em cada consulta — para que esquecer de
/// filtrar não seja uma possibilidade.
/// </summary>
public interface IContextoAcesso
{
    /// <summary>
    /// CNPJ autorizado, sem formatação. <see langword="null"/> significa contexto de
    /// sistema (consumidor da fila, migração) e é registrado em log quando usado.
    /// Requisições HTTP nunca chegam ao domínio com este valor nulo: o middleware
    /// de autenticação rejeita antes.
    /// </summary>
    string? CnpjAutorizado { get; }
}
