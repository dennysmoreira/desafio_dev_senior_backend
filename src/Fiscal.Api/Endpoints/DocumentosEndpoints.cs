using Fiscal.Application.Documentos;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;

namespace Fiscal.Api.Endpoints;

public static class DocumentosEndpoints
{
    /// <summary>
    /// Teto de upload. Uma NF-e real fica na casa de dezenas de KB; 10 MB dá folga
    /// para XMLs com centenas de itens e ainda barra tentativa de exaustão de
    /// memória bem antes de o parser ser acionado.
    /// </summary>
    public const long TamanhoMaximoDoXml = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapDocumentos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/documentos").WithTags("Documentos");

        grupo.MapPost(string.Empty, IngerirAsync)
            .WithSummary("Recebe um XML fiscal")
            .WithDescription(
                "Envie o XML no corpo com Content-Type application/xml. A operação é idempotente: "
                + "reenviar o mesmo arquivo devolve 200 com o documento já existente, sem duplicar. "
                + "Reenviar conteúdo diferente para a mesma chave de acesso devolve 409.")
            .Accepts<string>("application/xml")
            .Produces<DocumentoCriadoResponse>(StatusCodes.Status201Created)
            .Produces<DocumentoCriadoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return rotas;
    }

    private static async Task<IResult> IngerirAsync(
        HttpRequest requisicao,
        RegistrarDocumento caso,
        CancellationToken cancellationToken)
    {
        if (requisicao.ContentLength > TamanhoMaximoDoXml)
        {
            return Problema(
                StatusCodes.Status413PayloadTooLarge,
                "Arquivo grande demais",
                $"O limite é de {TamanhoMaximoDoXml / 1024 / 1024} MB.");
        }

        var xml = await LerCorpoAsync(requisicao, cancellationToken);

        if (xml.Length == 0)
        {
            return Problema(StatusCodes.Status400BadRequest, "Corpo vazio", "Envie o XML no corpo da requisição.");
        }

        RespostaIngestao resposta;

        try
        {
            resposta = await caso.ExecutarAsync(xml, cancellationToken);
        }
        catch (DomainException excecao)
        {
            // XML malformado, chave inválida, layout desconhecido. É erro do cliente,
            // não do servidor — e, no consumo da fila, é a classe de erro que NUNCA
            // deve ser retentada, porque nenhuma tentativa futura vai funcionar.
            return Problema(StatusCodes.Status422UnprocessableEntity, "XML inválido", excecao.Message);
        }

        return resposta.Resultado switch
        {
            ResultadoIngestao.Criado => Results.Created(
                $"/documentos/{resposta.Documento!.Id}",
                DocumentoCriadoResponse.De(resposta.Documento)),

            ResultadoIngestao.Repetido => Repetido(requisicao, resposta.Documento!),

            ResultadoIngestao.Divergente => Problema(
                StatusCodes.Status409Conflict,
                "Chave já registrada com outro conteúdo",
                "A chave de acesso já existe com um XML diferente. Documento fiscal autorizado é "
                + "imutável: divergência indica que um dos dois arquivos não é o documento oficial."),

            ResultadoIngestao.EmitenteNaoAutorizado => Problema(
                StatusCodes.Status403Forbidden,
                "Emitente não autorizado",
                "O CNPJ emitente do XML não corresponde ao CNPJ autenticado."),

            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Reenvio do mesmo XML. Responde 200, e não 409, porque a operação teve
    /// sucesso — só não hoje. Um cliente que perdeu a resposta da primeira chamada e
    /// está retentando precisa ver sucesso: bibliotecas de retry tratam 4xx como
    /// falha terminal e transformariam uma ingestão bem-sucedida em alarme. O 409
    /// fica reservado para o caso em que há de fato conflito — mesma chave, conteúdo
    /// diferente — para que os dois códigos carreguem informação distinta.
    /// </summary>
    private static IResult Repetido(HttpRequest requisicao, DocumentoFiscal documento)
    {
        requisicao.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";

        return Results.Ok(DocumentoCriadoResponse.De(documento));
    }

    private static async Task<ReadOnlyMemory<byte>> LerCorpoAsync(
        HttpRequest requisicao,
        CancellationToken cancellationToken)
    {
        // Buffer limitado mesmo sem Content-Length declarado — cliente pode omitir o
        // cabeçalho ou mentir nele. O limite do Kestrel é a segunda barreira.
        using var buffer = new MemoryStream();
        await requisicao.Body.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    private static IResult Problema(int status, string titulo, string detalhe) =>
        Results.Problem(statusCode: status, title: titulo, detail: detalhe);
}

/// <summary>
/// Resposta da ingestão. Não devolve o XML bruto nem o documento do destinatário
/// completo — a listagem e a criação trabalham sempre com dado mascarado.
/// </summary>
public sealed record DocumentoCriadoResponse(
    Guid Id,
    string Tipo,
    string ChaveAcesso,
    string Numero,
    string Serie,
    string CnpjEmitente,
    string NomeEmitente,
    string UfEmitente,
    string? DocumentoDestinatario,
    DateTimeOffset DataEmissao,
    decimal ValorTotal,
    int QuantidadeItens,
    string HashConteudo)
{
    public static DocumentoCriadoResponse De(DocumentoFiscal documento) => new(
        documento.Id,
        documento.Tipo.ToString(),
        documento.ChaveAcesso,
        documento.Numero,
        documento.Serie,
        documento.CnpjEmitente,
        documento.NomeEmitente,
        documento.UfEmitente,
        DadosSensiveis.Mascarar(documento.DocumentoDestinatario),
        documento.DataEmissao,
        documento.ValorTotal,
        documento.Itens.Count,
        documento.HashConteudo);
}
