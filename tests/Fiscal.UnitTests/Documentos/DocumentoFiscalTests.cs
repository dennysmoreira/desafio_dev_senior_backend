using System.Reflection;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;
using Fiscal.UnitTests.Recursos;

namespace Fiscal.UnitTests.Documentos;

[TestFixture]
public sealed class DocumentoFiscalTests
{
    private static readonly DateTimeOffset Agora = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A tese central do desenho — documento fiscal autorizado é imutável — está
    /// codificada na forma da classe, não numa convenção. Este teste falha se
    /// alguém abrir um setter público, que é exatamente como a regra se perderia.
    /// </summary>
    [Test]
    public void Nenhuma_propriedade_do_documento_tem_setter_publico()
    {
        var comSetterPublico = typeof(DocumentoFiscal)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(propriedade => propriedade.SetMethod?.IsPublic == true)
            .Select(propriedade => propriedade.Name)
            .ToArray();

        comSetterPublico.ShouldBeEmpty();
    }

    [Test]
    public void Registrar_exige_chave_de_acesso()
    {
        Should.Throw<DomainException>(() => Registrar(chave: " "));
    }

    [Test]
    public void Registrar_exige_cnpj_do_emitente()
    {
        Should.Throw<DomainException>(() => Registrar(cnpj: ""));
    }

    [Test]
    public void Registrar_exige_hash_do_conteudo()
    {
        Should.Throw<DomainException>(() => Registrar(hash: ""));
    }

    [Test]
    public void Documento_nasce_ativo_e_sem_observacao()
    {
        var documento = Registrar();

        using (Assert.EnterMultipleScope())
        {
            documento.Excluido.ShouldBeFalse();
            documento.ExcluidoEm.ShouldBeNull();
            documento.Observacao.ShouldBeNull();
            documento.RecebidoEm.ShouldBe(Agora);
        }
    }

    [Test]
    public void Atualizar_observacao_nao_altera_nenhum_campo_fiscal()
    {
        var documento = Registrar();

        var antes = (documento.ChaveAcesso, documento.CnpjEmitente, documento.ValorTotal,
            documento.DataEmissao, documento.HashConteudo, documento.Itens.Count);

        documento.AtualizarObservacao("conferido", Agora.AddHours(1));

        using (Assert.EnterMultipleScope())
        {
            documento.Observacao.ShouldBe("conferido");
            documento.AtualizadoEm.ShouldBe(Agora.AddHours(1));

            (documento.ChaveAcesso, documento.CnpjEmitente, documento.ValorTotal,
                documento.DataEmissao, documento.HashConteudo, documento.Itens.Count).ShouldBe(antes);
        }
    }

    [Test]
    public void Documento_excluido_recusa_alteracao_de_observacao()
    {
        var documento = Registrar();
        documento.Excluir(Agora);

        Should.Throw<DomainException>(() => documento.AtualizarObservacao("tarde demais", Agora));
    }

    [Test]
    public void Excluir_e_idempotente_e_preserva_a_data_da_primeira_exclusao()
    {
        var documento = Registrar();

        documento.Excluir(Agora);
        var primeiraExclusao = documento.ExcluidoEm;

        documento.Excluir(Agora.AddDays(1));

        using (Assert.EnterMultipleScope())
        {
            documento.Excluido.ShouldBeTrue();
            documento.ExcluidoEm.ShouldBe(primeiraExclusao);
        }
    }

    private static DocumentoFiscal Registrar(
        string? chave = null,
        string cnpj = NfeDeTeste.CnpjEmitentePadrao,
        string hash = "abc123") =>
        DocumentoFiscal.Registrar(
            TipoDocumentoFiscal.Nfe,
            chave ?? NfeDeTeste.Chave(),
            "1",
            "1",
            cnpj,
            "Comercio Exemplo Ltda",
            "SP",
            NfeDeTeste.CpfDestinatarioPadrao,
            "Maria Aparecida de Souza",
            Agora,
            300.00m,
            hash,
            [ItemDocumentoFiscal.Criar(1, "SKU-001", "Caderno", "48201000", "5102", 10m, 15.5m, 155m)],
            Agora);
}
