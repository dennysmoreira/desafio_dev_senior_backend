namespace Fiscal.Domain.Documentos;

/// <summary>
/// Item do documento. Imutável pela mesma razão que o cabeçalho: faz parte do
/// documento autorizado.
/// </summary>
public sealed class ItemDocumentoFiscal
{
    private ItemDocumentoFiscal()
    {
    }

    public Guid Id { get; private set; }

    public Guid DocumentoId { get; private set; }

    public int Numero { get; private set; }

    public string Codigo { get; private set; } = string.Empty;

    public string Descricao { get; private set; } = string.Empty;

    public string? Ncm { get; private set; }

    public string? Cfop { get; private set; }

    public decimal Quantidade { get; private set; }

    public decimal ValorUnitario { get; private set; }

    public decimal ValorTotal { get; private set; }

    public static ItemDocumentoFiscal Criar(
        int numero,
        string codigo,
        string descricao,
        string? ncm,
        string? cfop,
        decimal quantidade,
        decimal valorUnitario,
        decimal valorTotal) => new()
        {
            Id = Guid.CreateVersion7(),
            Numero = numero,
            Codigo = codigo,
            Descricao = descricao,
            Ncm = ncm,
            Cfop = cfop,
            Quantidade = quantidade,
            ValorUnitario = valorUnitario,
            ValorTotal = valorTotal,
        };
}
