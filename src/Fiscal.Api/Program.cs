using Fiscal.Api.Endpoints;
using Fiscal.Api.Seguranca;
using Fiscal.Infrastructure;
using Fiscal.Infrastructure.Mensageria;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Primeira barreira de tamanho, antes de qualquer código nosso rodar. A segunda
// está no endpoint, para o caso de o cliente omitir ou mentir no Content-Length.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = DocumentosEndpoints.TamanhoMaximoDoXml);

builder.Services.AddFiscalInfrastructure(
    builder.Configuration.GetConnectionString("Fiscal")
    ?? throw new InvalidOperationException("ConnectionStrings:Fiscal não configurada."));

// A API só PUBLICA. O consumidor vive em Fiscal.Worker, num processo separado, para
// que escalar a ingestão não multiplique consumidores e um deploy da API não
// interrompa mensagem em processamento.
//
// Sem string de conexão do broker a API sobe com um publicador que só registra em
// log. É o modo usado pelos testes de integração de ingestão, que não precisam de
// fila — mas o aviso no start impede que isso passe despercebido em produção.
var brokerUri = builder.Configuration.GetConnectionString("RabbitMq");

if (string.IsNullOrWhiteSpace(brokerUri))
{
    builder.Services.AddFiscalMensageriaEmLog();
}
else
{
    builder.Services.AddPublicadorRabbitMq(new OpcoesRabbitMq { Uri = brokerUri });
}

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

await app.Services.MigrarBancoAsync(CancellationToken.None);

// Erro não tratado vira ProblemDetails genérico. Sem isto, o Kestrel em ambiente
// de desenvolvimento devolve a página de exceção com stack trace, caminho de
// arquivos e nomes internos — informação que não deve sair do servidor.
app.UseExceptionHandler();

app.UseMiddleware<AutenticacaoPorCnpj>(
    app.Configuration["Autenticacao:ChaveDeApi"]
    ?? throw new InvalidOperationException("Autenticacao:ChaveDeApi não configurada."));

if (string.IsNullOrWhiteSpace(brokerUri))
{
    app.Logger.LogWarning(
        "ConnectionStrings:RabbitMq não configurada. Eventos serão apenas registrados em log, "
        + "sem chegar ao broker nem ao worker.");
}

app.MapOpenApi();
app.MapScalarApiReference();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.MapDocumentos();
app.MapConsultas();
app.MapResumos();

await app.RunAsync();

/// <summary>Exposto para o WebApplicationFactory dos testes de integração.</summary>
public partial class Program;
