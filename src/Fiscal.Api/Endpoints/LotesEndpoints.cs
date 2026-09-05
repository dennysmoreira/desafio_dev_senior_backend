using Fiscal.Application.Lotes;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Lotes;

namespace Fiscal.Api.Endpoints;

public static class LotesEndpoints
{
    /// <summary>Teto por arquivo. Uma NF-e real tem dezenas de KB; 10 MB dá folga larga.</summary>
    public const long TamanhoMaximoPorArquivo = 10 * 1024 * 1024;

    /// <summary>
    /// Teto da requisição inteira. Não é o produto do limite por arquivo pelo número
    /// máximo deles — seria 1 GB numa requisição, o que nenhum cliente honesto envia
    /// e todo atacante tentaria.
    /// </summary>
    public const long TamanhoMaximoDoLote = 50 * 1024 * 1024;

    public static IEndpointRouteBuilder MapLotes(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/lotes").WithTags("Lotes");

        grupo.MapPost(string.Empty, ReceberAsync)
            .WithSummary("Recebe um lote de XMLs fiscais")
            .WithDescription(
                "Envie os arquivos como multipart/form-data. A resposta é 202: os arquivos são "
                + "gravados e enfileirados, e o processamento acontece de forma assíncrona. "
                + "Acompanhe por GET /lotes/{id}.")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .Produces<LoteAceito>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .DisableAntiforgery();

        grupo.MapGet("/{id:guid}", ConsultarAsync)
            .WithSummary("Situação de um lote")
            .WithDescription(
                "Traz a situação do lote e de cada arquivo: pendente, ingerido, duplicado ou "
                + "rejeitado, com o motivo em caso de rejeição.")
            .Produces<SituacaoDoLote>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapGet(string.Empty, ListarAsync)
            .WithSummary("Lotes recentes do contribuinte autenticado")
            .Produces<IReadOnlyList<SituacaoDoLote>>();

        return rotas;
    }

    private static async Task<IResult> ReceberAsync(
        HttpRequest requisicao,
        ReceberLote caso,
        CancellationToken cancellationToken)
    {
        if (!requisicao.HasFormContentType)
        {
            return Problema(
                StatusCodes.Status400BadRequest,
                "Formato inesperado",
                "Envie os arquivos como multipart/form-data.");
        }

        IFormCollection formulario;

        try
        {
            formulario = await requisicao.ReadFormAsync(cancellationToken);
        }
        catch (Exception excecao) when (excecao is InvalidDataException or IOException)
        {
            // Corpo multipart malformado — inclusive o caso de vir sem nenhuma parte.
            // É erro do cliente; sem este tratamento vira 500 e o servidor assume a
            // culpa por um pedido que nunca foi válido.
            return Problema(
                StatusCodes.Status400BadRequest,
                "Corpo inválido",
                "Não foi possível ler o multipart/form-data enviado.");
        }

        if (formulario.Files.Count == 0)
        {
            return Problema(
                StatusCodes.Status400BadRequest,
                "Nenhum arquivo",
                "Envie ao menos um arquivo XML no lote.");
        }

        var grande = formulario.Files.FirstOrDefault(a => a.Length > TamanhoMaximoPorArquivo);

        if (grande is not null)
        {
            return Problema(
                StatusCodes.Status413PayloadTooLarge,
                "Arquivo grande demais",
                $"'{grande.FileName}' excede o limite de {TamanhoMaximoPorArquivo / 1024 / 1024} MB por arquivo.");
        }

        var arquivos = new List<ArquivoParaIngestao>(formulario.Files.Count);

        foreach (var enviado in formulario.Files)
        {
            using var buffer = new MemoryStream();
            await using (var origem = enviado.OpenReadStream())
            {
                await origem.CopyToAsync(buffer, cancellationToken);
            }

            arquivos.Add(new ArquivoParaIngestao(enviado.FileName, buffer.ToArray()));
        }

        try
        {
            var aceito = await caso.ExecutarAsync(arquivos, cancellationToken);

            // 202, não 201: nada foi processado ainda. O recurso que passa a existir
            // é o lote, não os documentos — e é ele que o Location aponta.
            return Results.Accepted($"/lotes/{aceito.LoteId}", aceito);
        }
        catch (DomainException excecao)
        {
            return Problema(StatusCodes.Status400BadRequest, "Lote recusado", excecao.Message);
        }
    }

    private static async Task<IResult> ConsultarAsync(
        Guid id,
        ConsultarLote caso,
        CancellationToken cancellationToken)
    {
        var situacao = await caso.ExecutarAsync(id, cancellationToken);

        // 404 também para lote de outro contribuinte: um 403 confirmaria a existência.
        return situacao is null
            ? Problema(
                StatusCodes.Status404NotFound,
                "Lote não encontrado",
                "Nenhum lote com este identificador para o CNPJ autenticado.")
            : Results.Ok(situacao);
    }

    private static async Task<IResult> ListarAsync(
        ListarLotes caso,
        CancellationToken cancellationToken,
        int quantidade = 20) =>
        Results.Ok(await caso.ExecutarAsync(Math.Clamp(quantidade, 1, LoteDeIngestao.MaximoDeArquivos), cancellationToken));

    private static IResult Problema(int status, string titulo, string detalhe) =>
        Results.Problem(statusCode: status, title: titulo, detail: detalhe);
}
