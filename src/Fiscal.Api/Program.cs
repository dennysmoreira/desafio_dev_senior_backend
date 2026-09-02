using Fiscal.Api.Endpoints;
using Fiscal.Api.Seguranca;
using Fiscal.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Primeira barreira de tamanho, antes de qualquer código nosso rodar. A segunda
// está no endpoint, para o caso de o cliente omitir ou mentir no Content-Length.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = DocumentosEndpoints.TamanhoMaximoDoXml);

builder.Services.AddFiscalInfrastructure(
    builder.Configuration.GetConnectionString("Fiscal")
    ?? throw new InvalidOperationException("ConnectionStrings:Fiscal não configurada."));

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

app.MapOpenApi();
app.MapScalarApiReference();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.MapDocumentos();
app.MapConsultas();

await app.RunAsync();

/// <summary>Exposto para o WebApplicationFactory dos testes de integração.</summary>
public partial class Program;
