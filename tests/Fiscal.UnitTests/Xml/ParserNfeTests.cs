using System.Text;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;
using Fiscal.Infrastructure.Xml;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.UnitTests.Xml;

[TestFixture]
public sealed class ParserNfeTests
{
    private readonly ParserNfe _parser = new();

    [Test]
    public void Extrai_cabecalho_e_itens_de_uma_nfe_valida()
    {
        var lido = _parser.Ler(NfeDeTeste.Bytes());

        using (Assert.EnterMultipleScope())
        {
            lido.Tipo.ShouldBe(TipoDocumentoFiscal.Nfe);
            lido.ChaveAcesso.ShouldBe(NfeDeTeste.Chave());
            lido.CnpjEmitente.ShouldBe(NfeDeTeste.CnpjEmitentePadrao);
            lido.NomeEmitente.ShouldBe("Comercio Exemplo Ltda");
            lido.UfEmitente.ShouldBe("SP");
            lido.DocumentoDestinatario.ShouldBe(NfeDeTeste.CpfDestinatarioPadrao);
            lido.ValorTotal.ShouldBe(300.00m);
            lido.Itens.Count.ShouldBe(2);
            lido.Itens[0].Descricao.ShouldBe("Caderno universitario");
            lido.Itens[0].Quantidade.ShouldBe(10m);
            lido.Itens[1].ValorTotal.ShouldBe(145.00m);
        }
    }

    [Test]
    public void Converte_a_data_de_emissao_para_utc()
    {
        // dhEmi vem com fuso brasileiro; timestamptz do Postgres guarda instante,
        // e o Npgsql recusa offset diferente de zero.
        var lido = _parser.Ler(NfeDeTeste.Bytes(dataEmissao: "2026-01-15T10:30:00-03:00"));

        using (Assert.EnterMultipleScope())
        {
            lido.DataEmissao.Offset.ShouldBe(TimeSpan.Zero);
            lido.DataEmissao.Hour.ShouldBe(13);
        }
    }

    [Test]
    public void Aceita_nfe_sem_destinatario()
    {
        var lido = _parser.Ler(NfeDeTeste.Bytes(cpfDestinatario: null));

        lido.DocumentoDestinatario.ShouldBeNull();
    }

    [Test]
    public void Recusa_chave_de_acesso_fora_dos_44_digitos()
    {
        var xml = NfeDeTeste.Texto().Replace($"NFe{NfeDeTeste.Chave()}", "NFe123", StringComparison.Ordinal);

        var erro = Should.Throw<DomainException>(() => _parser.Ler(Encoding.UTF8.GetBytes(xml)));

        erro.Message.ShouldContain("44 dígitos");
    }

    [Test]
    public void Recusa_xml_sem_infNFe()
    {
        var xml = Encoding.UTF8.GetBytes(
            """<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe /></nfeProc>""");

        Should.Throw<DomainException>(() => _parser.Ler(xml));
    }

    /// <summary>
    /// O ataque não pode só falhar — tem que falhar como erro de domínio, para virar
    /// 422 na API e ir direto para a dead-letter no consumidor. Se escapasse como
    /// XmlException viraria 500 e seria retentado indefinidamente.
    /// </summary>
    [Test]
    public void Recusa_ataque_xxe_como_erro_de_dominio()
    {
        var erro = Should.Throw<DomainException>(() => _parser.Ler(NfeDeTeste.ComAtaqueXxe()));

        erro.Message.ShouldContain("DTD");
    }

    [Test]
    public void Recusa_bomba_de_entidades_como_erro_de_dominio()
    {
        Should.Throw<DomainException>(() => _parser.Ler(NfeDeTeste.ComBombaDeEntidades()));
    }

    [Test]
    public void Recusa_conteudo_que_nao_e_xml()
    {
        Should.Throw<DomainException>(() => _parser.Ler(Encoding.UTF8.GetBytes("isto não é xml")));
    }

    [Test]
    public void Reconhece_apenas_o_namespace_da_nfe()
    {
        using (Assert.EnterMultipleScope())
        {
            _parser.Reconhece("nfeProc", "http://www.portalfiscal.inf.br/nfe").ShouldBeTrue();
            _parser.Reconhece("NFe", "http://www.portalfiscal.inf.br/nfe").ShouldBeTrue();
            _parser.Reconhece("cteProc", "http://www.portalfiscal.inf.br/cte").ShouldBeFalse();
            _parser.Reconhece("nfeProc", null).ShouldBeFalse();
        }
    }
}
