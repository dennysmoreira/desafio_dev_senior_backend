using Fiscal.Application.Mensageria;
using Microsoft.Extensions.Logging;

namespace Fiscal.Application.Resumos;

public interface IRepositorioResumos
{
    /// <summary>
    /// Registra o processamento da mensagem e acumula o documento no resumo — as
    /// duas coisas na MESMA transação.
    /// <para>
    /// Devolve <see langword="false"/> quando a mensagem já havia sido processada por
    /// este consumidor. A detecção vem da chave única do inbox falhando no banco, não
    /// de uma consulta prévia: entre consultar e gravar haveria janela para a mesma
    /// mensagem ser contada duas vezes.
    /// </para>
    /// </summary>
    Task<bool> AcumularAsync(
        string mensagemId,
        string consumidor,
        string cnpjEmitente,
        string competencia,
        decimal valorDocumento,
        DateTimeOffset agora,
        CancellationToken cancellationToken);

    /// <summary>Resumos do CNPJ autenticado. O filtro global cuida do isolamento.</summary>
    Task<IReadOnlyList<ResumoDoEmitente>> ListarAsync(CancellationToken cancellationToken);
}

public sealed record ResumoDoEmitente(
    string CnpjEmitente,
    string Competencia,
    int QuantidadeDocumentos,
    decimal ValorTotal,
    DateTimeOffset AtualizadoEm);

public sealed class ConsultarResumos(IRepositorioResumos repositorio)
{
    public Task<IReadOnlyList<ResumoDoEmitente>> ExecutarAsync(CancellationToken cancellationToken) =>
        repositorio.ListarAsync(cancellationToken);
}

/// <summary>
/// O "algo útil" do item 3: mantém um agregado por emitente e competência.
/// <para>
/// Escolhido de propósito por ser um acumulador. Se o consumidor não fosse
/// idempotente, uma reentrega inflaria silenciosamente a soma — o defeito
/// apareceria nos números, não numa exceção. É o que torna este recurso uma prova
/// do item 7 no lado do consumo, e não só uma demonstração do item 3.
/// </para>
/// </summary>
public sealed class AtualizarResumoDoEmitente(
    IRepositorioResumos repositorio,
    TimeProvider relogio,
    ILogger<AtualizarResumoDoEmitente> logger)
{
    public const string NomeDoConsumidor = "resumo-por-emitente";

    public async Task ExecutarAsync(DocumentoProcessado evento, CancellationToken cancellationToken)
    {
        var competencia = $"{evento.DataEmissao.Year:D4}-{evento.DataEmissao.Month:D2}";

        var aplicado = await repositorio.AcumularAsync(
            evento.MensagemId,
            NomeDoConsumidor,
            evento.CnpjEmitente,
            competencia,
            evento.ValorTotal,
            relogio.GetUtcNow(),
            cancellationToken);

        if (!aplicado)
        {
            logger.LogInformation(
                "Mensagem {MensagemId} já processada por {Consumidor}; reentrega ignorada.",
                evento.MensagemId,
                NomeDoConsumidor);
        }
    }
}
