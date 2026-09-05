namespace Fiscal.Application.Resumos;

public interface IRepositorioResumos
{
    /// <summary>
    /// Acumula um documento no resumo do emitente naquela competência, criando a
    /// linha se ainda não existir.
    /// <para>
    /// Não abre transação nem consulta inbox: quem chama é a ingestão, que já está
    /// dentro de uma transação cobrindo o inbox, o documento, o item, o lote e este
    /// acúmulo. Duplicar o controle aqui criaria transação aninhada e uma segunda
    /// fonte de verdade sobre "já processei".
    /// </para>
    /// </summary>
    Task AcumularAsync(
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
