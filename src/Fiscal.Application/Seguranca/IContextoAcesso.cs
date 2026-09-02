namespace Fiscal.Application.Seguranca;

/// <summary>
/// Quem está fazendo a chamada. O isolamento por CNPJ é aplicado no
/// <c>DbContext</c> a partir daqui — não em cada consulta — para que esquecer de
/// filtrar não seja uma possibilidade.
/// </summary>
public interface IContextoAcesso
{
    /// <summary>
    /// CNPJ autorizado, sem formatação, ou <see langword="null"/> quando há um
    /// escopo de sistema aberto (consumidor da fila, migração).
    /// <para>
    /// Ler esta propriedade sem CNPJ definido e sem escopo de sistema lança
    /// <see cref="AcessoNaoAutenticadoException"/>. É deliberado: o filtro global
    /// do <c>DbContext</c> lê daqui, então uma consulta feita fora de um contexto
    /// autenticado falha em vez de retornar dados de todos os CNPJs.
    /// </para>
    /// </summary>
    string? CnpjAutorizado { get; }
}

/// <summary>
/// Face de escrita do contexto. Separada da leitura para que o código de consulta
/// não consiga trocar de identidade no meio de uma requisição.
/// </summary>
public interface IDefinidorContextoAcesso
{
    void DefinirCnpj(string cnpj);

    /// <summary>
    /// Abre um escopo sem isolamento por CNPJ, para trabalho de sistema. Toda
    /// abertura é registrada em log com o motivo — é o rastro de auditoria de
    /// acesso cross-tenant.
    /// </summary>
    IDisposable AbrirEscopoDeSistema(string motivo);
}

public sealed class AcessoNaoAutenticadoException()
    : Exception("Consulta tentada sem CNPJ autorizado e fora de um escopo de sistema.");
