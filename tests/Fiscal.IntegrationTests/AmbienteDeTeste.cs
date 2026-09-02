using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Fiscal.IntegrationTests;

/// <summary>
/// Sobe um PostgreSQL real em container uma única vez para toda a suíte e liga a
/// API nele. Nada de banco em memória: metade do que esta suíte precisa provar —
/// índice único, filtro global, exclusão lógica — só existe no banco de verdade.
/// <para>
/// Exige Docker. O README documenta como pular esta suíte quem não tiver.
/// </para>
/// </summary>
[SetUpFixture]
public sealed class AmbienteDeTeste
{
    public const string ChaveDeApi = "chave-de-teste";

    private static PostgreSqlContainer _postgres = null!;
    private static WebApplicationFactory<Program> _fabrica = null!;

    [OneTimeSetUp]
    public async Task IniciarAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("fiscal")
            .Build();

        await _postgres.StartAsync();

        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(construtor =>
        {
            construtor.UseSetting("ConnectionStrings:Fiscal", _postgres.GetConnectionString());
            construtor.UseSetting("Autenticacao:ChaveDeApi", ChaveDeApi);
        });

        // Força a construção do host, que aplica as migrations no start.
        _ = _fabrica.CreateClient();
    }

    [OneTimeTearDown]
    public async Task PararAsync()
    {
        await _fabrica.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Cliente autenticado como o contribuinte informado.</summary>
    public static HttpClient Cliente(string cnpj)
    {
        var cliente = _fabrica.CreateClient();

        cliente.DefaultRequestHeaders.Add("X-Api-Key", ChaveDeApi);
        cliente.DefaultRequestHeaders.Add("X-Cnpj", cnpj);

        return cliente;
    }

    public static StringContent CorpoXml(byte[] xml) =>
        new(Encoding.UTF8.GetString(xml), Encoding.UTF8, MediaTypeHeaderValue.Parse("application/xml"));
}
