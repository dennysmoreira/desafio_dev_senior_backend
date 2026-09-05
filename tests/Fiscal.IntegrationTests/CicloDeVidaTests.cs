using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.IntegrationTests;

[TestFixture]
public sealed class CicloDeVidaTests
{
    private const string Cnpj = "44444444000144";

    private HttpClient _cliente = null!;

    [SetUp]
    public void Preparar() => _cliente = AmbienteDeTeste.Cliente(Cnpj);

    [TearDown]
    public void Encerrar() => _cliente.Dispose();

    [Test]
    public async Task A_listagem_mascara_o_destinatario_e_o_detalhe_devolve_integro()
    {
        var id = await IngerirAsync(30);

        var pagina = await _cliente.GetFromJsonAsync<JsonElement>("/documentos?tamanho=100");

        var naListagem = pagina.GetProperty("itens").EnumerateArray()
            .First(item => item.GetProperty("id").GetGuid() == id)
            .GetProperty("documentoDestinatario").GetString();

        var detalhe = await _cliente.GetFromJsonAsync<JsonElement>($"/documentos/{id}");
        var noDetalhe = detalhe.GetProperty("documentoDestinatario").GetString();

        using (Assert.EnterMultipleScope())
        {
            naListagem.ShouldNotBeNull();
            naListagem.ShouldNotBe(NfeDeTeste.CpfDestinatarioPadrao);
            naListagem!.ShouldContain("*");
            noDetalhe.ShouldBe(NfeDeTeste.CpfDestinatarioPadrao);
        }
    }

    [Test]
    public async Task O_etag_evita_transferir_o_documento_de_novo()
    {
        var id = await IngerirAsync(31);

        var primeira = await _cliente.GetAsync($"/documentos/{id}");
        var etag = primeira.Headers.ETag!.ToString();

        using var requisicao = new HttpRequestMessage(HttpMethod.Get, $"/documentos/{id}");
        requisicao.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));

        var segunda = await _cliente.SendAsync(requisicao);

        segunda.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task Alterar_a_observacao_muda_o_etag_mas_nao_o_documento_fiscal()
    {
        var id = await IngerirAsync(32);

        var antes = await _cliente.GetAsync($"/documentos/{id}");
        var etagAntes = antes.Headers.ETag!.ToString();
        var original = await antes.Content.ReadFromJsonAsync<JsonElement>();

        await _cliente.PutAsJsonAsync($"/documentos/{id}", new { observacao = "conferido" });

        var depois = await _cliente.GetAsync($"/documentos/{id}");
        var atualizado = await depois.Content.ReadFromJsonAsync<JsonElement>();

        using (Assert.EnterMultipleScope())
        {
            atualizado.GetProperty("observacao").GetString().ShouldBe("conferido");
            depois.Headers.ETag!.ToString().ShouldNotBe(etagAntes);

            // Nada do documento fiscal se moveu.
            atualizado.GetProperty("chaveAcesso").GetString()
                .ShouldBe(original.GetProperty("chaveAcesso").GetString());
            atualizado.GetProperty("valorTotal").GetDecimal()
                .ShouldBe(original.GetProperty("valorTotal").GetDecimal());
            atualizado.GetProperty("hashConteudo").GetString()
                .ShouldBe(original.GetProperty("hashConteudo").GetString());
            atualizado.GetProperty("itens").GetArrayLength()
                .ShouldBe(original.GetProperty("itens").GetArrayLength());
        }
    }

    [Test]
    public async Task Excluir_esconde_da_listagem_mas_a_chave_continua_ocupada()
    {
        var chave = NfeDeTeste.Chave(33, Cnpj);
        var xml = NfeDeTeste.Bytes(chave, Cnpj);

        var id = await AmbienteDeTeste.IngerirDocumentoAsync(_cliente, xml);

        var exclusao = await _cliente.DeleteAsync($"/documentos/{id}");
        var repetida = await _cliente.DeleteAsync($"/documentos/{id}");
        var detalhe = await _cliente.GetAsync($"/documentos/{id}");
        var alteracao = await _cliente.PutAsJsonAsync($"/documentos/{id}", new { observacao = "x" });

        // O ponto sutil: o índice único não filtra exclusão lógica, então reenviar o
        // XML não recria a linha. Se filtrasse, existiriam dois registros para o
        // mesmo documento fiscal — e a idempotência morreria no caso menos óbvio.
        // No fluxo em lote isso aparece como um item marcado "Duplicado".
        var reenvio = await AmbienteDeTeste.IngerirLoteAsync(_cliente, ("reenvio.xml", xml));

        var pagina = await _cliente.GetFromJsonAsync<JsonElement>("/documentos?tamanho=100");
        var aindaVisivel = pagina.GetProperty("itens").EnumerateArray()
            .Any(item => item.GetProperty("id").GetGuid() == id);

        using (Assert.EnterMultipleScope())
        {
            exclusao.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            repetida.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            detalhe.StatusCode.ShouldBe(HttpStatusCode.Gone);
            alteracao.StatusCode.ShouldBe(HttpStatusCode.Gone);
            reenvio.Duplicados.ShouldBe(1);
            reenvio.Itens[0].DocumentoId.ShouldBe(id);
            aindaVisivel.ShouldBeFalse();
        }
    }

    [Test]
    public async Task A_listagem_filtra_por_destinatario_e_por_periodo()
    {
        await IngerirAsync(34);

        var porDestinatario = await _cliente.GetFromJsonAsync<JsonElement>(
            $"/documentos?documentoDestinatario={NfeDeTeste.CpfDestinatarioPadrao}");

        var deOutroDestinatario = await _cliente.GetFromJsonAsync<JsonElement>(
            "/documentos?documentoDestinatario=00000000000");

        var foraDoPeriodo = await _cliente.GetFromJsonAsync<JsonElement>(
            "/documentos?dataInicio=2030-01-01T00:00:00Z");

        using (Assert.EnterMultipleScope())
        {
            porDestinatario.GetProperty("total").GetInt32().ShouldBeGreaterThan(0);
            deOutroDestinatario.GetProperty("total").GetInt32().ShouldBe(0);
            foraDoPeriodo.GetProperty("total").GetInt32().ShouldBe(0);
        }
    }

    [Test]
    public async Task O_tamanho_de_pagina_e_limitado_ao_teto()
    {
        var pagina = await _cliente.GetFromJsonAsync<JsonElement>("/documentos?tamanho=1000000");

        pagina.GetProperty("tamanho").GetInt32().ShouldBe(100);
    }

    [Test]
    public async Task Documento_inexistente_devolve_404()
    {
        var resposta = await _cliente.GetAsync($"/documentos/{Guid.NewGuid()}");

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private Task<Guid> IngerirAsync(int sequencial) =>
        AmbienteDeTeste.IngerirDocumentoAsync(
            _cliente, NfeDeTeste.Bytes(NfeDeTeste.Chave(sequencial, Cnpj), Cnpj));
}
