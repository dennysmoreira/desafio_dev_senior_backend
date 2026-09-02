using Fiscal.Application.Documentos;
using Fiscal.Domain.Documentos;

namespace Fiscal.Api.Endpoints;

public static class ConsultaEndpoints
{
    public static IEndpointRouteBuilder MapConsultas(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/documentos").WithTags("Documentos");

        grupo.MapGet(string.Empty, ListarAsync)
            .WithSummary("Lista documentos com paginação e filtros")
            .WithDescription(
                "Filtros por período de emissão, CNPJ/CPF do destinatário e UF do emitente. "
                + $"Tamanho de página padrão {FiltroDocumentos.TamanhoPadrao} e máximo {FiltroDocumentos.TamanhoMaximo}. "
                + "O CNPJ/CPF do destinatário vem mascarado — o valor íntegro só no detalhe.")
            .Produces<PaginaDe<ResumoDocumento>>();

        grupo.MapGet("/{id:guid}", ObterAsync)
            .WithSummary("Detalhe de um documento")
            .WithDescription("Devolve ETag; reenvie em If-None-Match para receber 304 quando nada mudou.")
            .Produces<DocumentoDetalheResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        grupo.MapPut("/{id:guid}", AtualizarAsync)
            .WithSummary("Atualiza a observação interna")
            .WithDescription(
                "Documento fiscal autorizado é imutável: chave, emitente, valores e itens não são "
                + "alteráveis por este endpoint nem por nenhum outro. A observação é anotação de "
                + "gestão, nasce do nosso processo e não do Fisco.")
            .Produces<DocumentoDetalheResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        grupo.MapDelete("/{id:guid}", ExcluirAsync)
            .WithSummary("Exclui logicamente um documento")
            .WithDescription(
                "Exclusão lógica. O Fisco exige guarda de 5 anos, então a linha permanece e continua "
                + "ocupando sua chave de acesso. Repetir a chamada é no-op.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        ConsultarDocumentos caso,
        CancellationToken cancellationToken,
        DateTimeOffset? dataInicio = null,
        DateTimeOffset? dataFim = null,
        string? documentoDestinatario = null,
        string? uf = null,
        int pagina = 1,
        int tamanho = FiltroDocumentos.TamanhoPadrao)
    {
        var filtro = new FiltroDocumentos
        {
            DataInicio = dataInicio,
            DataFim = dataFim,
            DocumentoDestinatario = documentoDestinatario,
            Uf = uf,
            Pagina = pagina,
            Tamanho = tamanho,
        };

        return Results.Ok(await caso.ExecutarAsync(filtro, cancellationToken));
    }

    private static async Task<IResult> ObterAsync(
        Guid id,
        HttpRequest requisicao,
        ObterDocumento caso,
        CancellationToken cancellationToken)
    {
        var resposta = await caso.ExecutarAsync(id, cancellationToken);

        if (resposta.Resultado is not ResultadoConsulta.Encontrado)
        {
            return Falha(resposta.Resultado);
        }

        var documento = resposta.Documento!;

        // O SHA-256 já calculado na ingestão vira ETag sem custo adicional. Como o
        // documento fiscal é imutável, a única coisa que invalida o cache é a
        // observação — por isso ela entra na etiqueta.
        var etag = $"\"{documento.HashConteudo}-{documento.AtualizadoEm.Ticks}\"";

        if (requisicao.Headers.IfNoneMatch.Contains(etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        requisicao.HttpContext.Response.Headers.ETag = etag;

        return Results.Ok(DocumentoDetalheResponse.De(documento));
    }

    private static async Task<IResult> AtualizarAsync(
        Guid id,
        AtualizarObservacaoRequest corpo,
        AtualizarObservacaoDocumento caso,
        CancellationToken cancellationToken)
    {
        var resposta = await caso.ExecutarAsync(id, corpo.Observacao, cancellationToken);

        return resposta.Resultado is ResultadoConsulta.Encontrado
            ? Results.Ok(DocumentoDetalheResponse.De(resposta.Documento!))
            : Falha(resposta.Resultado);
    }

    private static async Task<IResult> ExcluirAsync(
        Guid id,
        ExcluirDocumento caso,
        CancellationToken cancellationToken)
    {
        var resultado = await caso.ExecutarAsync(id, cancellationToken);

        return resultado is ResultadoConsulta.NaoEncontrado
            ? Falha(resultado)
            : Results.NoContent();
    }

    private static IResult Falha(ResultadoConsulta resultado) => resultado switch
    {
        ResultadoConsulta.Excluido => Results.Problem(
            statusCode: StatusCodes.Status410Gone,
            title: "Documento excluído",
            detail: "O documento existiu e foi excluído logicamente. A exclusão não é revertida por esta API."),

        // Também para documento de outro CNPJ: um 403 confirmaria a existência do
        // registro, o que já é informação demais entre contribuintes distintos.
        _ => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Documento não encontrado",
            detail: "Nenhum documento com este identificador para o CNPJ autenticado."),
    };
}

public sealed record AtualizarObservacaoRequest(string? Observacao);

/// <summary>
/// Detalhe. Diferente da listagem, traz o CPF/CNPJ do destinatário íntegro — o
/// acesso é registrado em log pelo caso de uso.
/// </summary>
public sealed record DocumentoDetalheResponse(
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
    string HashConteudo,
    string? Observacao,
    DateTimeOffset RecebidoEm,
    DateTimeOffset AtualizadoEm,
    IReadOnlyList<ItemResponse> Itens)
{
    public static DocumentoDetalheResponse De(DocumentoFiscal documento) => new(
        documento.Id,
        documento.Tipo.ToString(),
        documento.ChaveAcesso,
        documento.Numero,
        documento.Serie,
        documento.CnpjEmitente,
        documento.NomeEmitente,
        documento.UfEmitente,
        documento.DocumentoDestinatario,
        documento.NomeDestinatario,
        documento.DataEmissao,
        documento.ValorTotal,
        documento.HashConteudo,
        documento.Observacao,
        documento.RecebidoEm,
        documento.AtualizadoEm,
        [.. documento.Itens.OrderBy(item => item.Numero).Select(ItemResponse.De)]);
}

public sealed record ItemResponse(
    int Numero,
    string Codigo,
    string Descricao,
    string? Ncm,
    string? Cfop,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal)
{
    public static ItemResponse De(ItemDocumentoFiscal item) => new(
        item.Numero,
        item.Codigo,
        item.Descricao,
        item.Ncm,
        item.Cfop,
        item.Quantidade,
        item.ValorUnitario,
        item.ValorTotal);
}
