using Fiscal.Application.Resumos;

namespace Fiscal.Api.Endpoints;

public static class ResumoEndpoints
{
    public static IEndpointRouteBuilder MapResumos(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/resumos", async (ConsultarResumos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ExecutarAsync(cancellationToken)))
            .WithTags("Resumos")
            .WithSummary("Resumo por competência, alimentado pelo consumidor da fila")
            .WithDescription(
                "Agregado mantido pelo consumidor de DocumentoProcessado, não pela ingestão. "
                + "Serve para observar o efeito do consumo: se a contagem divergir do número de "
                + "documentos ingeridos, o consumidor processou alguma mensagem duas vezes.")
            .Produces<IReadOnlyList<ResumoDoEmitente>>();

        return rotas;
    }
}
