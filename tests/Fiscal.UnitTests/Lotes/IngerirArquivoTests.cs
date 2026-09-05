using System.Text;
using Fiscal.Application.Lotes;
using Fiscal.Domain.Lotes;
using Fiscal.Infrastructure.Xml;
using Fiscal.UnitTests.Dubles;
using Fiscal.UnitTests.Recursos;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fiscal.UnitTests.Lotes;

/// <summary>
/// Cobre os cinco desfechos da ingestão sem banco, sem fila e sem storage real.
/// </summary>
[TestFixture]
public sealed class IngerirArquivoTests
{
    private ArmazenamentoFalso _armazenamento = null!;
    private RepositorioDocumentosFalso _documentos = null!;
    private RepositorioLotesFalso _lotes = null!;
    private RepositorioResumosFalso _resumos = null!;
    private IngerirArquivo _caso = null!;

    [SetUp]
    public void Preparar()
    {
        _armazenamento = new ArmazenamentoFalso();
        _documentos = new RepositorioDocumentosFalso();
        _lotes = new RepositorioLotesFalso();
        _resumos = new RepositorioResumosFalso();

        _caso = new IngerirArquivo(
            _armazenamento,
            new SeletorDeParser([new ParserNfe()]),
            _documentos,
            _lotes,
            _resumos,
            new ProcessamentoFalso(),
            new UnidadeDeTrabalhoFalsa(),
            TimeProvider.System,
            NullLogger<IngerirArquivo>.Instance);
    }

    [Test]
    public async Task Arquivo_valido_e_ingerido_e_alimenta_o_resumo()
    {
        var mensagem = await PrepararArquivoAsync(NfeDeTeste.Bytes());

        var resultado = await _caso.ExecutarAsync(mensagem, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resultado.ShouldBe(ResultadoDaIngestao.Ingerido);
            _documentos.Documentos.Count.ShouldBe(1);
            _resumos.Acumulos.Count.ShouldBe(1);
            ItemDe(mensagem).Situacao.ShouldBe(SituacaoItem.Ingerido);
            _lotes.Lotes[0].Situacao.ShouldBe(SituacaoLote.Concluido);
        }
    }

    [Test]
    public async Task Reentrega_da_mesma_mensagem_nao_conta_o_documento_duas_vezes()
    {
        var mensagem = await PrepararArquivoAsync(NfeDeTeste.Bytes());

        await _caso.ExecutarAsync(mensagem, CancellationToken.None);
        var segunda = await _caso.ExecutarAsync(mensagem, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            segunda.ShouldBe(ResultadoDaIngestao.JaProcessado);
            _documentos.Documentos.Count.ShouldBe(1);

            // O resumo é acumulador: se o inbox falhasse, este número iria a 2 e o
            // defeito apareceria em silêncio, sem exceção nenhuma.
            _resumos.Acumulos.Count.ShouldBe(1);
        }
    }

    [Test]
    public async Task Arquivo_ja_ingerido_em_outro_lote_vira_duplicado_e_nao_erro()
    {
        var xml = NfeDeTeste.Bytes();

        await _caso.ExecutarAsync(await PrepararArquivoAsync(xml), CancellationToken.None);
        var segundoEnvio = await PrepararArquivoAsync(xml);

        var resultado = await _caso.ExecutarAsync(segundoEnvio, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resultado.ShouldBe(ResultadoDaIngestao.Duplicado);
            _documentos.Documentos.Count.ShouldBe(1);
            ItemDe(segundoEnvio).Situacao.ShouldBe(SituacaoItem.Duplicado);
            _resumos.Acumulos.Count.ShouldBe(1);
        }
    }

    [Test]
    public async Task Mesma_chave_com_conteudo_diferente_e_rejeitada_com_motivo()
    {
        var chave = NfeDeTeste.Chave();

        await _caso.ExecutarAsync(
            await PrepararArquivoAsync(NfeDeTeste.Bytes(chave, valorTotal: 300m)), CancellationToken.None);

        var divergente = await PrepararArquivoAsync(NfeDeTeste.Bytes(chave, valorTotal: 999.99m));
        var resultado = await _caso.ExecutarAsync(divergente, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resultado.ShouldBe(ResultadoDaIngestao.Rejeitado);
            _documentos.Documentos.Count.ShouldBe(1);
            _documentos.Documentos[0].ValorTotal.ShouldBe(300m);
            ItemDe(divergente).Motivo.ShouldNotBeNull().ShouldContain("XML diferente");
            _lotes.Lotes[^1].Situacao.ShouldBe(SituacaoLote.ConcluidoComErros);
        }
    }

    [Test]
    public async Task Xml_de_outro_emitente_e_rejeitado_sem_gravar_documento()
    {
        var mensagem = await PrepararArquivoAsync(NfeDeTeste.Bytes(cnpjEmitente: "99999999000199"));

        var resultado = await _caso.ExecutarAsync(mensagem, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resultado.ShouldBe(ResultadoDaIngestao.Rejeitado);
            _documentos.Documentos.ShouldBeEmpty();
            ItemDe(mensagem).Motivo.ShouldNotBeNull().ShouldContain("não corresponde");
        }
    }

    [Test]
    public async Task Xml_malformado_e_rejeitado_com_o_motivo_visivel_no_lote()
    {
        var mensagem = await PrepararArquivoAsync(Encoding.UTF8.GetBytes("isto não é xml"));

        var resultado = await _caso.ExecutarAsync(mensagem, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resultado.ShouldBe(ResultadoDaIngestao.Rejeitado);
            ItemDe(mensagem).Motivo.ShouldNotBeNull().ShouldContain("XML inválido");
        }
    }

    [Test]
    public async Task Ataque_xxe_e_rejeitado_como_item_e_nao_derruba_o_lote()
    {
        var mensagem = await PrepararArquivoAsync(NfeDeTeste.ComAtaqueXxe());

        var resultado = await _caso.ExecutarAsync(mensagem, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resultado.ShouldBe(ResultadoDaIngestao.Rejeitado);
            ItemDe(mensagem).Motivo.ShouldNotBeNull().ShouldContain("DTD");
        }
    }

    [Test]
    public async Task Arquivo_ausente_no_storage_e_rejeitado_e_nao_explode()
    {
        var lote = LoteDeIngestao.Registrar(NfeDeTeste.CnpjEmitentePadrao, DateTimeOffset.UtcNow);
        var item = lote.Adicionar("sumiu.xml", "chave-que-nao-existe", 10);
        _lotes.Lotes.Add(lote);

        var resultado = await _caso.ExecutarAsync(
            new ArquivoRecebido(lote.Id, item.Id, item.ChaveDeArmazenamento, lote.CnpjProprietario, "sumiu.xml"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resultado.ShouldBe(ResultadoDaIngestao.Rejeitado);
            item.Motivo.ShouldNotBeNull().ShouldContain("não encontrado");
        }
    }

    /// <summary>Grava no storage falso e cria o lote com um item, como a API faria.</summary>
    private async Task<ArquivoRecebido> PrepararArquivoAsync(byte[] xml)
    {
        var chave = Fiscal.Domain.Comum.HashConteudo.Calcular(xml);

        await _armazenamento.GravarAsync(chave, xml, CancellationToken.None);

        var lote = LoteDeIngestao.Registrar(NfeDeTeste.CnpjEmitentePadrao, DateTimeOffset.UtcNow);
        var item = lote.Adicionar("nfe.xml", chave, xml.Length);

        _lotes.Lotes.Add(lote);

        return new ArquivoRecebido(lote.Id, item.Id, chave, lote.CnpjProprietario, "nfe.xml");
    }

    private ItemDoLote ItemDe(ArquivoRecebido mensagem) =>
        _lotes.Lotes.SelectMany(l => l.Itens).Single(i => i.Id == mensagem.ItemId);
}
