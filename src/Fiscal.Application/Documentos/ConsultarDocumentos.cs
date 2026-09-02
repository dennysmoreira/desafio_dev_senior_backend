using Fiscal.Domain.Comum;

namespace Fiscal.Application.Documentos;

/// <summary>
/// Filtros da listagem.
/// <para>
/// Não existe filtro por CNPJ do emitente: ele já está fixado pelo filtro global,
/// então o parâmetro seria inócuo. O filtro por CNPJ/CPF que produz resultado útil
/// é o do destinatário — "quais documentos emiti para o cliente X".
/// </para>
/// </summary>
public sealed record FiltroDocumentos
{
    public const int TamanhoPadrao = 20;

    /// <summary>
    /// Teto de página. Sem ele, um cliente pede tamanho=1000000 e transforma a
    /// listagem num vetor de exaustão de memória do servidor.
    /// </summary>
    public const int TamanhoMaximo = 100;

    public DateTimeOffset? DataInicio { get; init; }

    public DateTimeOffset? DataFim { get; init; }

    public string? DocumentoDestinatario { get; init; }

    public string? Uf { get; init; }

    public int Pagina { get; init; } = 1;

    public int Tamanho { get; init; } = TamanhoPadrao;

    /// <summary>Aplica os limites. Entrada inválida vira o valor válido mais próximo, não erro.</summary>
    public FiltroDocumentos Normalizado() => this with
    {
        Pagina = Math.Max(1, Pagina),
        Tamanho = Math.Clamp(Tamanho, 1, TamanhoMaximo),
        DocumentoDestinatario = SomenteDigitos(DocumentoDestinatario),
        Uf = string.IsNullOrWhiteSpace(Uf) ? null : Uf.Trim().ToUpperInvariant(),
    };

    private static string? SomenteDigitos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var digitos = new string(valor.Where(char.IsAsciiDigit).ToArray());

        return digitos.Length == 0 ? null : digitos;
    }
}

public sealed record ResumoDocumento(
    Guid Id,
    string Tipo,
    string ChaveAcesso,
    string Numero,
    string Serie,
    string CnpjEmitente,
    string NomeEmitente,
    string UfEmitente,
    string? DocumentoDestinatario,
    string? NomeDestinatario,
    DateTimeOffset DataEmissao,
    decimal ValorTotal,
    string? Observacao);

public sealed record PaginaDe<T>(IReadOnlyList<T> Itens, int Pagina, int Tamanho, int Total)
{
    public int TotalPaginas => Tamanho == 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamanho);
}

public sealed class ConsultarDocumentos(IRepositorioDocumentos repositorio)
{
    public async Task<PaginaDe<ResumoDocumento>> ExecutarAsync(
        FiltroDocumentos filtro,
        CancellationToken cancellationToken)
    {
        var pagina = await repositorio.ListarAsync(filtro.Normalizado(), cancellationToken);

        // Mascaramento acontece aqui, não no endpoint: assim nenhum caminho que
        // consuma a listagem — inclusive um log de diagnóstico — vê CPF em claro.
        // Exposição em massa é o risco; o valor íntegro só sai no detalhe.
        return pagina with
        {
            Itens =
            [
                .. pagina.Itens.Select(item => item with
                {
                    DocumentoDestinatario = DadosSensiveis.Mascarar(item.DocumentoDestinatario),
                    NomeDestinatario = DadosSensiveis.MascararNome(item.NomeDestinatario),
                }),
            ],
        };
    }
}
