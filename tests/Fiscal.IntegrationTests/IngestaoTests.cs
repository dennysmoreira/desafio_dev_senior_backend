using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.IntegrationTests;

[TestFixture]
public sealed class IngestaoTests
{
    private const string Cnpj = "11111111000191";

    private HttpClient _cliente = null!;

    [SetUp]
    public void Preparar() => _cliente = AmbienteDeTeste.Cliente(Cnpj);

    [TearDown]
    public void Encerrar() => _cliente.Dispose();

    [Test]
    public async Task O_mesmo_xml_enviado_duas_vezes_produz_um_unico_documento()
    {
        var chave = NfeDeTeste.Chave(10, Cnpj);
        var xml = NfeDeTeste.Bytes(chave, Cnpj);

        var primeira = await _cliente.PostAsync("/documentos", AmbienteDeTeste.CorpoXml(xml));
        var segunda = await _cliente.PostAsync("/documentos", AmbienteDeTeste.CorpoXml(xml));

        using (Assert.EnterMultipleScope())
        {
            primeira.StatusCode.ShouldBe(HttpStatusCode.Created);
            segunda.StatusCode.ShouldBe(HttpStatusCode.OK);

            // Sinaliza que nada foi criado, sem transformar sucesso em erro para
            // quem está retentando por ter perdido a resposta da primeira chamada.
            segunda.Headers.GetValues("X-Idempotent-Replay").ShouldContain("true");

            // Conta ocorrências desta chave, não o total do CNPJ: os demais testes
            // desta fixture usam o mesmo emitente e povoam a mesma base.
            (await ContarOcorrenciasDaChaveAsync(chave)).ShouldBe(1);
        }
    }

    [Test]
    public async Task Envios_simultaneos_do_mesmo_xml_gravam_uma_linha_so()
    {
        // O caso que mata a implementação ingênua: com SELECT antes do INSERT, várias
        // requisições passam na verificação ao mesmo tempo e todas tentam gravar.
        // Aqui quem decide é o índice único, então não há janela.
        var xml = NfeDeTeste.Bytes(NfeDeTeste.Chave(11, Cnpj), Cnpj);

        var respostas = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ =>
                AmbienteDeTeste.Cliente(Cnpj).PostAsync("/documentos", AmbienteDeTeste.CorpoXml(xml))));

        var criados = respostas.Count(r => r.StatusCode == HttpStatusCode.Created);
        var repetidos = respostas.Count(r => r.StatusCode == HttpStatusCode.OK);

        using (Assert.EnterMultipleScope())
        {
            criados.ShouldBe(1);
            repetidos.ShouldBe(19);
            respostas.ShouldAllBe(r => r.IsSuccessStatusCode);
        }
    }

    [Test]
    public async Task Mesma_chave_com_conteudo_diferente_e_recusada_e_nao_sobrescreve()
    {
        var chave = NfeDeTeste.Chave(12, Cnpj);

        await _cliente.PostAsync(
            "/documentos",
            AmbienteDeTeste.CorpoXml(NfeDeTeste.Bytes(chave, Cnpj, valorTotal: 300.00m)));

        var divergente = await _cliente.PostAsync(
            "/documentos",
            AmbienteDeTeste.CorpoXml(NfeDeTeste.Bytes(chave, Cnpj, valorTotal: 999.99m)));

        var documento = await LocalizarPorChaveAsync(chave);

        using (Assert.EnterMultipleScope())
        {
            divergente.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            documento.GetProperty("valorTotal").GetDecimal().ShouldBe(300.00m);
        }
    }

    [Test]
    public async Task Xml_de_outro_emitente_e_recusado_com_403()
    {
        var resposta = await _cliente.PostAsync(
            "/documentos",
            AmbienteDeTeste.CorpoXml(NfeDeTeste.Bytes(NfeDeTeste.Chave(13), cnpjEmitente: "99999999000199")));

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Ataque_xxe_e_recusado_como_erro_do_cliente()
    {
        var resposta = await _cliente.PostAsync(
            "/documentos",
            AmbienteDeTeste.CorpoXml(NfeDeTeste.ComAtaqueXxe()));

        // 422 e não 500: a culpa é do cliente, e um 500 faria o consumidor tratar
        // como falha transitória e retentar para sempre.
        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task Sem_chave_de_api_a_requisicao_e_recusada()
    {
        using var anonimo = AmbienteDeTeste.Cliente(Cnpj);
        anonimo.DefaultRequestHeaders.Remove("X-Api-Key");

        var resposta = await anonimo.PostAsync(
            "/documentos",
            AmbienteDeTeste.CorpoXml(NfeDeTeste.Bytes(NfeDeTeste.Chave(14, Cnpj), Cnpj)));

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<int> ContarOcorrenciasDaChaveAsync(string chave)
    {
        var pagina = await _cliente.GetFromJsonAsync<JsonElement>("/documentos?tamanho=100");

        return pagina.GetProperty("itens").EnumerateArray()
            .Count(item => item.GetProperty("chaveAcesso").GetString() == chave);
    }

    private async Task<JsonElement> LocalizarPorChaveAsync(string chave)
    {
        var pagina = await _cliente.GetFromJsonAsync<JsonElement>("/documentos?tamanho=100");

        return pagina.GetProperty("itens").EnumerateArray()
            .First(item => item.GetProperty("chaveAcesso").GetString() == chave);
    }
}
