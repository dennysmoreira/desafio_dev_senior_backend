using System.Text;
using Fiscal.Application.Documentos;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;
using Fiscal.Infrastructure.Xml;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.UnitTests.Xml;

[TestFixture]
public sealed class SeletorDeParserTests
{
    [Test]
    public void Escolhe_o_parser_de_nfe_para_um_xml_de_nfe()
    {
        var seletor = new SeletorDeParser([new ParserNfe()]);

        seletor.Selecionar(NfeDeTeste.Bytes()).ShouldBeOfType<ParserNfe>();
    }

    /// <summary>
    /// A prova de que acrescentar CT-e ou NFS-e é escrever uma classe e registrá-la:
    /// este parser falso é de um layout que não existe no projeto, e o pipeline o
    /// seleciona sem que nenhuma linha do seletor ou da ingestão tenha mudado.
    /// </summary>
    [Test]
    public void Seleciona_um_layout_novo_apenas_por_registro_no_container()
    {
        var seletor = new SeletorDeParser([new ParserNfe(), new ParserFalsoDeCte()]);

        var xmlCte = Encoding.UTF8.GetBytes(
            """<cteProc xmlns="http://www.portalfiscal.inf.br/cte"><x /></cteProc>""");

        seletor.Selecionar(xmlCte).ShouldBeOfType<ParserFalsoDeCte>();
    }

    [Test]
    public void Recusa_layout_sem_parser_registrado()
    {
        var seletor = new SeletorDeParser([new ParserNfe()]);

        var erro = Should.Throw<DomainException>(
            () => seletor.Selecionar(Encoding.UTF8.GetBytes("<boleto><x /></boleto>")));

        erro.Message.ShouldContain("boleto");
    }

    [Test]
    public void Recusa_xxe_ja_na_identificacao_do_layout()
    {
        // A blindagem age antes de qualquer parser ver o documento.
        var seletor = new SeletorDeParser([new ParserNfe()]);

        Should.Throw<DomainException>(() => seletor.Selecionar(NfeDeTeste.ComAtaqueXxe()));
    }

    private sealed class ParserFalsoDeCte : IParserDocumentoFiscal
    {
        public bool Reconhece(string elementoRaiz, string? namespaceRaiz) =>
            elementoRaiz == "cteProc";

        public DocumentoFiscalLido Ler(ReadOnlyMemory<byte> xml) =>
            throw new NotSupportedException("Não é preciso ler: o teste comprova só a seleção.");
    }
}
