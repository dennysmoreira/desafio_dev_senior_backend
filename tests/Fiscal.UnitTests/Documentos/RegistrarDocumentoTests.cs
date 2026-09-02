using Fiscal.Application.Documentos;
using Fiscal.Application.Mensageria;
using Fiscal.Application.Seguranca;
using Fiscal.Domain.Documentos;
using Fiscal.Infrastructure.Xml;
using Fiscal.UnitTests.Dubles;
using Fiscal.UnitTests.Recursos;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fiscal.UnitTests.Documentos;

/// <summary>
/// Cobre as quatro saídas da ingestão sem banco nem fila. O repositório falso imita
/// o que importa: um índice único que recusa chave repetida.
/// </summary>
[TestFixture]
public sealed class RegistrarDocumentoTests
{
    private RepositorioFalso _repositorio = null!;
    private PublicadorFalso _publicador = null!;
    private ContextoFalso _contexto = null!;
    private RegistrarDocumento _caso = null!;

    [SetUp]
    public void Preparar()
    {
        _repositorio = new RepositorioFalso();
        _publicador = new PublicadorFalso();
        _contexto = new ContextoFalso { CnpjAutorizado = NfeDeTeste.CnpjEmitentePadrao };

        _caso = new RegistrarDocumento(
            new SeletorDeParser([new ParserNfe()]),
            _repositorio,
            _publicador,
            _contexto,
            TimeProvider.System,
            NullLogger<RegistrarDocumento>.Instance);
    }

    [Test]
    public async Task Primeiro_envio_cria_o_documento_e_publica_o_evento()
    {
        var resposta = await _caso.ExecutarAsync(NfeDeTeste.Bytes(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resposta.Resultado.ShouldBe(ResultadoIngestao.Criado);
            _repositorio.Documentos.Count.ShouldBe(1);
            _publicador.Publicados.Count.ShouldBe(1);
        }
    }

    [Test]
    public async Task Reenvio_identico_nao_duplica_e_nao_republica_o_evento()
    {
        var xml = NfeDeTeste.Bytes();

        await _caso.ExecutarAsync(xml, CancellationToken.None);
        var segunda = await _caso.ExecutarAsync(xml, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            segunda.Resultado.ShouldBe(ResultadoIngestao.Repetido);
            _repositorio.Documentos.Count.ShouldBe(1);

            // O efeito colateral já aconteceu na primeira vez. Republicar faria o
            // consumidor recontar o documento no resumo.
            _publicador.Publicados.Count.ShouldBe(1);
        }
    }

    [Test]
    public async Task Mesma_chave_com_conteudo_diferente_e_recusada_sem_gravar()
    {
        var chave = NfeDeTeste.Chave();

        await _caso.ExecutarAsync(NfeDeTeste.Bytes(chave, valorTotal: 300.00m), CancellationToken.None);
        var divergente = await _caso.ExecutarAsync(
            NfeDeTeste.Bytes(chave, valorTotal: 999.99m),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            divergente.Resultado.ShouldBe(ResultadoIngestao.Divergente);
            _repositorio.Documentos.Count.ShouldBe(1);
            _repositorio.Documentos[0].ValorTotal.ShouldBe(300.00m);
        }
    }

    [Test]
    public async Task Xml_de_outro_emitente_e_recusado_e_nada_e_gravado()
    {
        var resposta = await _caso.ExecutarAsync(
            NfeDeTeste.Bytes(cnpjEmitente: "99999999000199"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            resposta.Resultado.ShouldBe(ResultadoIngestao.EmitenteNaoAutorizado);
            _repositorio.Documentos.ShouldBeEmpty();
            _publicador.Publicados.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task A_identidade_da_mensagem_e_estavel_entre_ingestoes_do_mesmo_xml()
    {
        // O consumidor reconhece reentrega pelo MessageId. Se ele mudasse a cada
        // publicação, o inbox nunca detectaria duplicata.
        var xml = NfeDeTeste.Bytes();

        await _caso.ExecutarAsync(xml, CancellationToken.None);
        var mensagem = _publicador.Publicados[0].MensagemId;

        _repositorio.Documentos.Clear();
        await _caso.ExecutarAsync(xml, CancellationToken.None);

        _publicador.Publicados[1].MensagemId.ShouldBe(mensagem);
    }
}
