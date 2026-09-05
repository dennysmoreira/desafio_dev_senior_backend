using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.Application.Lotes;
using Fiscal.Domain.Lotes;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.IntegrationTests;

/// <summary>
/// Ingestão ponta a ponta contra PostgreSQL e MinIO reais, exercitando o caminho
/// síncrono (API) e o assíncrono (o que o worker faria), sem broker no meio.
/// </summary>
[TestFixture]
public sealed class IngestaoTests
{
    private const string Cnpj = NfeDeTeste.CnpjEmitentePadrao;

    [Test]
    public async Task Lote_aceito_responde_202_e_deixa_os_itens_pendentes()
    {
        var cliente = AmbienteDeTeste.Cliente(Cnpj);

        var resposta = await cliente.PostAsync(
            "/lotes",
            AmbienteDeTeste.Lote(("a.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(9001)))));

        var aceito = await resposta.Content.ReadFromJsonAsync<LoteAceito>();
        var situacao = await ObterAsync(cliente, aceito!.LoteId);

        using (Assert.EnterMultipleScope())
        {
            resposta.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            resposta.Headers.Location!.ToString().ShouldContain(aceito.LoteId.ToString());
            situacao.Situacao.ShouldBe(nameof(SituacaoLote.Recebido));
            situacao.Pendentes.ShouldBe(1);
        }
    }

    [Test]
    public async Task Lote_com_varios_arquivos_cria_um_item_por_arquivo()
    {
        var cliente = AmbienteDeTeste.Cliente(Cnpj);

        var resposta = await cliente.PostAsync(
            "/lotes",
            AmbienteDeTeste.Lote(
                ("um.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(9010))),
                ("dois.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(9011))),
                ("tres.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(9012)))));

        var aceito = await resposta.Content.ReadFromJsonAsync<LoteAceito>();
        var situacao = await ObterAsync(cliente, aceito!.LoteId);

        using (Assert.EnterMultipleScope())
        {
            aceito.QuantidadeDeArquivos.ShouldBe(3);
            situacao.Total.ShouldBe(3);
            situacao.Itens.Select(i => i.NomeArquivo).ShouldBe(["dois.xml", "tres.xml", "um.xml"]);
        }
    }

    [Test]
    public async Task Lote_sem_arquivo_e_recusado()
    {
        var cliente = AmbienteDeTeste.Cliente(Cnpj);

        var resposta = await cliente.PostAsync("/lotes", AmbienteDeTeste.Lote());

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Processar_o_lote_ingere_o_documento_e_conclui()
    {
        var cliente = AmbienteDeTeste.Cliente(Cnpj);
        var chave = NfeDeTeste.Chave(9020);

        var aceito = await EnviarAsync(cliente, ("nfe.xml", NfeDeTeste.Bytes(chave)));
        await AmbienteDeTeste.ProcessarAsync(aceito.LoteId);

        var situacao = await ObterAsync(cliente, aceito.LoteId);

        using (Assert.EnterMultipleScope())
        {
            situacao.Situacao.ShouldBe(nameof(SituacaoLote.Concluido));
            situacao.Ingeridos.ShouldBe(1);
            situacao.Itens[0].DocumentoId.ShouldNotBeNull();
        }

        var listagem = await cliente.GetFromJsonAsync<JsonElement>($"/documentos?documentoDestinatario={NfeDeTeste.CpfDestinatarioPadrao}");

        listagem.GetProperty("total").GetInt32().ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Mesmo_arquivo_em_dois_lotes_grava_um_documento_e_marca_o_segundo_como_duplicado()
    {
        var cliente = AmbienteDeTeste.Cliente(Cnpj);
        var xml = NfeDeTeste.Bytes(NfeDeTeste.Chave(9030));

        var primeiro = await EnviarAsync(cliente, ("a.xml", xml));
        await AmbienteDeTeste.ProcessarAsync(primeiro.LoteId);

        var segundo = await EnviarAsync(cliente, ("a-de-novo.xml", xml));
        await AmbienteDeTeste.ProcessarAsync(segundo.LoteId);

        var situacao = await ObterAsync(cliente, segundo.LoteId);

        using (Assert.EnterMultipleScope())
        {
            situacao.Duplicados.ShouldBe(1);

            // Duplicata não é erro: o lote conclui sem marca de falha.
            situacao.Situacao.ShouldBe(nameof(SituacaoLote.Concluido));
        }
    }

    [Test]
    public async Task Xml_invalido_rejeita_o_item_e_o_lote_conclui_com_erros()
    {
        var cliente = AmbienteDeTeste.Cliente(Cnpj);

        var aceito = await EnviarAsync(
            cliente,
            ("bom.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(9040))),
            ("ruim.xml", "não sou xml"u8.ToArray()));

        await AmbienteDeTeste.ProcessarAsync(aceito.LoteId);

        var situacao = await ObterAsync(cliente, aceito.LoteId);

        using (Assert.EnterMultipleScope())
        {
            situacao.Situacao.ShouldBe(nameof(SituacaoLote.ConcluidoComErros));
            situacao.Ingeridos.ShouldBe(1);
            situacao.Rejeitados.ShouldBe(1);

            // O arquivo bom passou mesmo com o ruim ao lado: um item não contamina
            // o outro, que é a razão de haver uma mensagem por arquivo.
            situacao.Itens.Single(i => i.NomeArquivo == "ruim.xml").Motivo.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task Reprocessar_a_mesma_mensagem_nao_conta_o_documento_duas_vezes()
    {
        var cliente = AmbienteDeTeste.Cliente(Cnpj);

        var aceito = await EnviarAsync(cliente, ("nfe.xml", NfeDeTeste.Bytes(NfeDeTeste.Chave(9050))));

        // Duas passadas: é o que o RabbitMQ faz numa reentrega.
        await AmbienteDeTeste.ProcessarAsync(aceito.LoteId);
        await AmbienteDeTeste.ProcessarAsync(aceito.LoteId);

        var situacao = await ObterAsync(cliente, aceito.LoteId);

        using (Assert.EnterMultipleScope())
        {
            situacao.Ingeridos.ShouldBe(1);
            situacao.Situacao.ShouldBe(nameof(SituacaoLote.Concluido));
        }
    }

    private static async Task<LoteAceito> EnviarAsync(HttpClient cliente, params (string, byte[])[] arquivos)
    {
        var resposta = await cliente.PostAsync("/lotes", AmbienteDeTeste.Lote(arquivos));

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<LoteAceito>())!;
    }

    private static async Task<SituacaoDoLote> ObterAsync(HttpClient cliente, Guid id) =>
        (await cliente.GetFromJsonAsync<SituacaoDoLote>($"/lotes/{id}"))!;

}
