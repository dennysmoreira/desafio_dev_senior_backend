using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.IntegrationTests;

/// <summary>
/// O teste que justifica o filtro global. Isolamento aplicado no <c>DbContext</c>
/// vale para toda consulta escrita daqui pra frente; isolamento aplicado no
/// endpoint vale até alguém esquecer.
/// </summary>
[TestFixture]
public sealed class IsolamentoTests
{
    private const string CnpjA = "22222222000122";
    private const string CnpjB = "33333333000133";

    private Guid _documentoDeA;

    [OneTimeSetUp]
    public async Task PrepararAsync()
    {
        using var clienteA = AmbienteDeTeste.Cliente(CnpjA);

        var resposta = await clienteA.PostAsync(
            "/documentos",
            AmbienteDeTeste.CorpoXml(NfeDeTeste.Bytes(NfeDeTeste.Chave(20, CnpjA), CnpjA)));

        var criado = await resposta.Content.ReadFromJsonAsync<JsonElement>();

        _documentoDeA = criado.GetProperty("id").GetGuid();
    }

    [Test]
    public async Task Um_contribuinte_nao_lista_documentos_de_outro()
    {
        using var clienteB = AmbienteDeTeste.Cliente(CnpjB);

        var pagina = await clienteB.GetFromJsonAsync<JsonElement>("/documentos?tamanho=100");

        var chavesVisiveis = pagina.GetProperty("itens").EnumerateArray()
            .Select(item => item.GetProperty("cnpjEmitente").GetString())
            .Distinct();

        chavesVisiveis.ShouldNotContain(CnpjA);
    }

    [Test]
    public async Task Consultar_documento_de_outro_contribuinte_devolve_404_e_nao_403()
    {
        using var clienteB = AmbienteDeTeste.Cliente(CnpjB);

        var resposta = await clienteB.GetAsync($"/documentos/{_documentoDeA}");

        // 403 confirmaria que o documento existe. Entre contribuintes distintos,
        // isso já é vazamento — a resposta tem que ser indistinguível de "não existe".
        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Alterar_documento_de_outro_contribuinte_devolve_404()
    {
        using var clienteB = AmbienteDeTeste.Cliente(CnpjB);

        var resposta = await clienteB.PutAsJsonAsync(
            $"/documentos/{_documentoDeA}",
            new { observacao = "invadindo" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Excluir_documento_de_outro_contribuinte_devolve_404()
    {
        using var clienteB = AmbienteDeTeste.Cliente(CnpjB);

        var resposta = await clienteB.DeleteAsync($"/documentos/{_documentoDeA}");

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task O_dono_continua_enxergando_o_proprio_documento()
    {
        // Contraprova: sem ela, um filtro que escondesse tudo de todos passaria.
        using var clienteA = AmbienteDeTeste.Cliente(CnpjA);

        var resposta = await clienteA.GetAsync($"/documentos/{_documentoDeA}");

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
