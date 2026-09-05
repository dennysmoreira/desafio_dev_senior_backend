using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.IntegrationTests;

/// <summary>
/// O resumo é um acumulador, e foi escolhido assim de propósito: se a ingestão não
/// fosse idempotente, uma reentrega inflaria a soma em silêncio — o defeito
/// apareceria nos números, não numa exceção. Estes testes olham exatamente os
/// números.
/// </summary>
[TestFixture]
public sealed class ResumoDoConsumidorTests
{
    private const string Cnpj = "55555555000155";

    private HttpClient _cliente = null!;

    [SetUp]
    public void Preparar() => _cliente = AmbienteDeTeste.Cliente(Cnpj);

    [TearDown]
    public void Encerrar() => _cliente.Dispose();

    [Test]
    public async Task Documentos_do_mesmo_periodo_acumulam_no_mesmo_resumo()
    {
        await AmbienteDeTeste.IngerirLoteAsync(
            _cliente,
            ("um.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(60, Cnpj), Cnpj, valorTotal: 100m)),
            ("dois.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(61, Cnpj), Cnpj, valorTotal: 250m)));

        var resumo = await ObterAsync("2026-01");

        using (Assert.EnterMultipleScope())
        {
            resumo.GetProperty("quantidadeDocumentos").GetInt32().ShouldBe(2);
            resumo.GetProperty("valorTotal").GetDecimal().ShouldBe(350m);
        }
    }

    [Test]
    public async Task Reprocessar_o_lote_inteiro_nao_conta_nada_duas_vezes()
    {
        var cnpj = "56565656000156";
        using var cliente = AmbienteDeTeste.Cliente(cnpj);

        var situacao = await AmbienteDeTeste.IngerirLoteAsync(
            cliente, ("nfe.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(62, cnpj), cnpj, valorTotal: 400m)));

        // Segunda passada, como o RabbitMQ faria numa reentrega.
        await AmbienteDeTeste.ProcessarAsync(
            (await cliente.GetFromJsonAsync<JsonElement>("/lotes")).EnumerateArray().First()
                .GetProperty("id").GetGuid());

        var resumo = await ObterAsync("2026-01", cliente, cnpj);

        using (Assert.EnterMultipleScope())
        {
            situacao.Ingeridos.ShouldBe(1);
            resumo.GetProperty("quantidadeDocumentos").GetInt32().ShouldBe(1);
            resumo.GetProperty("valorTotal").GetDecimal().ShouldBe(400m);
        }
    }

    [Test]
    public async Task Documento_rejeitado_nao_entra_no_resumo()
    {
        var cnpj = "57575757000157";
        using var cliente = AmbienteDeTeste.Cliente(cnpj);

        await AmbienteDeTeste.IngerirLoteAsync(
            cliente,
            ("bom.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(63, cnpj), cnpj, valorTotal: 500m)),
            ("ruim.xml", "lixo"u8.ToArray()));

        var resumo = await ObterAsync("2026-01", cliente, cnpj);

        using (Assert.EnterMultipleScope())
        {
            resumo.GetProperty("quantidadeDocumentos").GetInt32().ShouldBe(1);
            resumo.GetProperty("valorTotal").GetDecimal().ShouldBe(500m);
        }
    }

    [Test]
    public async Task Um_contribuinte_nao_ve_o_resumo_de_outro()
    {
        using var vizinho = AmbienteDeTeste.Cliente("58585858000158");

        var resumos = await vizinho.GetFromJsonAsync<JsonElement>("/resumos");

        resumos.EnumerateArray()
            .Select(r => r.GetProperty("cnpjEmitente").GetString())
            .ShouldNotContain(Cnpj);
    }

    private async Task<JsonElement> ObterAsync(
        string competencia,
        HttpClient? cliente = null,
        string? cnpj = null)
    {
        var resumos = await (cliente ?? _cliente).GetFromJsonAsync<JsonElement>("/resumos");

        return resumos.EnumerateArray().Single(r =>
            r.GetProperty("cnpjEmitente").GetString() == (cnpj ?? Cnpj)
            && r.GetProperty("competencia").GetString() == competencia);
    }
}
