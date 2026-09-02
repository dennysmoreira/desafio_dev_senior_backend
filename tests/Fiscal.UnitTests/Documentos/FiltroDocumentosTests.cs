using Fiscal.Application.Documentos;

namespace Fiscal.UnitTests.Documentos;

[TestFixture]
public sealed class FiltroDocumentosTests
{
    [Test]
    public void Limita_o_tamanho_da_pagina_ao_teto()
    {
        // Sem teto, tamanho=1000000 vira um vetor de exaustão de memória.
        var filtro = new FiltroDocumentos { Tamanho = 1_000_000 }.Normalizado();

        filtro.Tamanho.ShouldBe(FiltroDocumentos.TamanhoMaximo);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Corrige_pagina_invalida_para_a_primeira(int pagina) =>
        new FiltroDocumentos { Pagina = pagina }.Normalizado().Pagina.ShouldBe(1);

    [Test]
    public void Normaliza_documento_do_destinatario_para_apenas_digitos() =>
        new FiltroDocumentos { DocumentoDestinatario = "529.982.247-25" }
            .Normalizado().DocumentoDestinatario.ShouldBe("52998224725");

    [Test]
    public void Trata_filtro_em_branco_como_ausente()
    {
        var filtro = new FiltroDocumentos { DocumentoDestinatario = "   ", Uf = "" }.Normalizado();

        using (Assert.EnterMultipleScope())
        {
            filtro.DocumentoDestinatario.ShouldBeNull();
            filtro.Uf.ShouldBeNull();
        }
    }

    [Test]
    public void Padroniza_a_uf_em_maiusculas() =>
        new FiltroDocumentos { Uf = " sp " }.Normalizado().Uf.ShouldBe("SP");
}
