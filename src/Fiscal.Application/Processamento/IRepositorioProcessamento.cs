namespace Fiscal.Application.Processamento;

public interface IRepositorioProcessamento
{
    /// <summary>
    /// Registra que esta mensagem foi processada por este consumidor. Devolve
    /// <see langword="false"/> quando já havia registro.
    /// <para>
    /// A detecção vem da chave única falhando no banco, não de uma consulta prévia:
    /// entre consultar e gravar haveria janela para dois consumidores concorrentes
    /// processarem a mesma mensagem. Deve ser chamado dentro da mesma transação do
    /// efeito colateral — é isso que torna o par indivisível.
    /// </para>
    /// </summary>
    Task<bool> TentarRegistrarAsync(
        string mensagemId,
        string consumidor,
        DateTimeOffset agora,
        CancellationToken cancellationToken);
}
