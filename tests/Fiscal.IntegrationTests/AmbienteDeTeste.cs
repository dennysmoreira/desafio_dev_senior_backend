using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fiscal.Application.Lotes;
using Fiscal.Application.Seguranca;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace Fiscal.IntegrationTests;

/// <summary>
/// Sobe PostgreSQL e MinIO reais em container, uma única vez para toda a suíte, e
/// liga a API neles. Nada de banco em memória nem storage falso: metade do que esta
/// suíte precisa provar — índice único, filtro global, exclusão lógica, savepoint
/// dentro de transação — só existe na infraestrutura de verdade.
/// <para>
/// Exige Docker. O README documenta como pular esta suíte quem não tiver.
/// </para>
/// </summary>
[SetUpFixture]
public sealed class AmbienteDeTeste
{
    public const string ChaveDeApi = "chave-de-teste";

    private static PostgreSqlContainer _postgres = null!;
    private static MinioContainer _minio = null!;
    private static WebApplicationFactory<Program> _fabrica = null!;

    [OneTimeSetUp]
    public async Task IniciarAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("fiscal")
            .Build();

        _minio = new MinioBuilder("minio/minio:RELEASE.2025-04-22T22-12-26Z").Build();

        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());

        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(construtor =>
        {
            construtor.UseSetting("ConnectionStrings:Fiscal", _postgres.GetConnectionString());
            construtor.UseSetting("Autenticacao:ChaveDeApi", ChaveDeApi);

            // Sem broker nesta suíte: a entrega em si é responsabilidade do RabbitMQ,
            // e o que precisa ser provado aqui é o efeito colateral do consumo, que os
            // testes exercitam chamando o caso de uso direto. Deixar a string
            // configurada faria o consumidor tentar conectar e travar a suíte inteira.
            construtor.UseSetting("ConnectionStrings:RabbitMq", string.Empty);

            construtor.UseSetting("Armazenamento:Endpoint", _minio.GetConnectionString());
            construtor.UseSetting("Armazenamento:AccessKey", _minio.GetAccessKey());
            construtor.UseSetting("Armazenamento:SecretKey", _minio.GetSecretKey());
        });

        // Força a construção do host, que aplica as migrations no start.
        _ = _fabrica.CreateClient();
    }

    [OneTimeTearDown]
    public async Task PararAsync()
    {
        await _fabrica.DisposeAsync();
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
    }

    /// <summary>Cliente autenticado como o contribuinte informado.</summary>
    public static HttpClient Cliente(string cnpj)
    {
        var cliente = _fabrica.CreateClient();

        cliente.DefaultRequestHeaders.Add("X-Api-Key", ChaveDeApi);
        cliente.DefaultRequestHeaders.Add("X-Cnpj", cnpj);

        return cliente;
    }

    /// <summary>Escopo de serviços da aplicação, para exercitar casos de uso direto.</summary>
    public static IServiceScope CriarEscopo() => _fabrica.Services.CreateScope();

    /// <summary>
    /// Envia um lote e o processa, devolvendo a situação final. É o atalho que
    /// substitui o antigo POST síncrono na maioria dos testes.
    /// </summary>
    public static async Task<SituacaoDoLote> IngerirLoteAsync(
        HttpClient cliente,
        params (string Nome, byte[] Xml)[] arquivos)
    {
        var resposta = await cliente.PostAsync("/lotes", Lote(arquivos));

        resposta.EnsureSuccessStatusCode();

        var aceito = (await resposta.Content.ReadFromJsonAsync<LoteAceito>())!;

        await ProcessarAsync(aceito.LoteId);

        return (await cliente.GetFromJsonAsync<SituacaoDoLote>($"/lotes/{aceito.LoteId}"))!;
    }

    /// <summary>Ingere um XML e devolve o id do documento resultante.</summary>
    public static async Task<Guid> IngerirDocumentoAsync(
        HttpClient cliente,
        byte[] xml,
        string nome = "nfe.xml")
    {
        var situacao = await IngerirLoteAsync(cliente, (nome, xml));

        return situacao.Itens[0].DocumentoId
            ?? throw new InvalidOperationException($"Item não ingerido: {situacao.Itens[0].Motivo}");
    }

    /// <summary>
    /// Faz o que o worker faria: executa a ingestão de cada item pendente. Sem
    /// broker — a entrega é responsabilidade do RabbitMQ e é exercitada contra a
    /// stack real; o que precisa ser provado aqui é o efeito.
    /// </summary>
    public static async Task ProcessarAsync(Guid loteId)
    {
        using var escopo = CriarEscopo();

        var definidor = escopo.ServiceProvider.GetRequiredService<IDefinidorContextoAcesso>();
        using var _ = definidor.AbrirEscopoDeSistema("processamento nos testes");

        var lotes = escopo.ServiceProvider.GetRequiredService<IRepositorioLotes>();
        var caso = escopo.ServiceProvider.GetRequiredService<IngerirArquivo>();

        var lote = await lotes.ObterAsync(loteId, CancellationToken.None);

        foreach (var item in lote!.Itens)
        {
            await caso.ExecutarAsync(
                new ArquivoRecebido(
                    lote.Id, item.Id, item.ChaveDeArmazenamento, lote.CnpjProprietario, item.NomeArquivo),
                CancellationToken.None);
        }
    }

    /// <summary>Monta um multipart com os arquivos informados, como o cliente faria.</summary>
    public static MultipartFormDataContent Lote(params (string Nome, byte[] Xml)[] arquivos)
    {
        var corpo = new MultipartFormDataContent();

        foreach (var (nome, xml) in arquivos)
        {
            var parte = new ByteArrayContent(xml);
            parte.Headers.ContentType = MediaTypeHeaderValue.Parse("application/xml");

            corpo.Add(parte, "arquivos", nome);
        }

        return corpo;
    }
}
