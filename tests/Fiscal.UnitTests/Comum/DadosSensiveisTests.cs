using System.Text;
using Fiscal.Domain.Comum;

namespace Fiscal.UnitTests.Comum;

[TestFixture]
public sealed class DadosSensiveisTests
{
    [TestCase("52998224725", "***982247**", TestName = "CPF mantém só o miolo")]
    [TestCase("529.982.247-25", "***982247**", TestName = "CPF formatado é normalizado antes")]
    [TestCase("12345678000199", "**345678******", TestName = "CNPJ mantém só o miolo")]
    [TestCase("", "", TestName = "vazio continua vazio")]
    [TestCase(null, "", TestName = "nulo vira vazio")]
    public void Mascara_documentos(string? entrada, string esperado) =>
        DadosSensiveis.Mascarar(entrada).ShouldBe(esperado);

    [Test]
    public void Documento_mascarado_nunca_revela_o_original()
    {
        const string cpf = "52998224725";

        var mascarado = DadosSensiveis.Mascarar(cpf);

        using (Assert.EnterMultipleScope())
        {
            mascarado.ShouldNotBe(cpf);
            mascarado.ShouldContain("*");

            // Os dígitos preservados não podem bastar para reconstruir o documento.
            mascarado.Count(char.IsAsciiDigit).ShouldBeLessThan(cpf.Length);
        }
    }

    [TestCase("Maria Aparecida de Souza", "Maria ********* ** *****")]
    [TestCase("Joana", "Joana")]
    [TestCase(null, "")]
    public void Mascara_nomes_preservando_o_primeiro(string? entrada, string esperado) =>
        DadosSensiveis.MascararNome(entrada).ShouldBe(esperado);
}

[TestFixture]
public sealed class HashConteudoTests
{
    [Test]
    public void Bytes_identicos_produzem_o_mesmo_hash()
    {
        var primeiro = HashConteudo.Calcular("<nfe />"u8);
        var segundo = HashConteudo.Calcular("<nfe />"u8);

        primeiro.ShouldBe(segundo);
    }

    [Test]
    public void Um_unico_byte_diferente_muda_o_hash()
    {
        // É o que separa reenvio idêntico (200) de conteúdo divergente (409).
        var original = HashConteudo.Calcular(Encoding.UTF8.GetBytes("<vNF>300.00</vNF>"));
        var alterado = HashConteudo.Calcular(Encoding.UTF8.GetBytes("<vNF>300.01</vNF>"));

        original.ShouldNotBe(alterado);
    }

    [Test]
    public void Produz_sha256_em_hexadecimal_minusculo()
    {
        var hash = HashConteudo.Calcular("qualquer coisa"u8);

        using (Assert.EnterMultipleScope())
        {
            hash.Length.ShouldBe(64);
            hash.ShouldBe(hash.ToLowerInvariant());
        }
    }
}
