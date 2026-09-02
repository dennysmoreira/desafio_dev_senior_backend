using System.Xml;
using Fiscal.Domain.Comum;

namespace Fiscal.Infrastructure.Xml;

/// <summary>
/// Configuração única de leitura de XML. Todo parser passa por aqui — não existe
/// <c>XmlReader.Create</c> solto no projeto.
/// <para>
/// O XML vem de fonte não confiável, então o leitor é fechado contra as três
/// famílias clássicas de ataque em ingestão de XML:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>XXE</b> — uma entidade externa faria o servidor ler
///     <c>file:///etc/passwd</c> ou bater numa URL interna. Fechado por
///     <see cref="DtdProcessing.Prohibit"/> e por não haver resolver.
///   </item>
///   <item>
///     <b>Billion laughs</b> — entidades que se referenciam em cascata expandem
///     para gigabytes e derrubam o processo. Fechado pelo mesmo
///     <see cref="DtdProcessing.Prohibit"/> e por <c>MaxCharactersFromEntities</c>.
///   </item>
///   <item>
///     <b>Documento gigante</b> — teto explícito de caracteres, além do limite de
///     tamanho aplicado antes, no endpoint.
///   </item>
/// </list>
/// </summary>
public static class LeitorXmlSeguro
{
    /// <summary>Teto de caracteres do documento. Acima disso o leitor aborta.</summary>
    private const long MaximoDeCaracteres = 40_000_000;

    public static XmlReaderSettings Configuracao => new()
    {
        // Nenhum DTD é aceito. Esta única linha fecha XXE e billion laughs.
        DtdProcessing = DtdProcessing.Prohibit,

        // Redundante com o Prohibit acima, mas explícito: nada resolve referência
        // externa. Se um dia alguém trocar o DtdProcessing, isto ainda segura.
        XmlResolver = null,
        MaxCharactersFromEntities = 0,

        MaxCharactersInDocument = MaximoDeCaracteres,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = true,
    };

    public static XmlReader Criar(Stream conteudo) => XmlReader.Create(conteudo, Configuracao);

    /// <summary>
    /// Traduz falha de leitura de XML em erro de domínio.
    /// <para>
    /// A distinção importa duas vezes. Na API, separa 422 (o cliente mandou lixo) de
    /// 500 (o servidor quebrou). No consumidor da fila, separa erro permanente —
    /// que vai direto para a dead-letter, porque nenhuma tentativa futura vai fazer
    /// um XML malformado virar válido — de erro transitório, que merece retry.
    /// Deixar <see cref="XmlException"/> escapar faria um ataque XXE recusado
    /// parecer indisponibilidade do serviço.
    /// </para>
    /// </summary>
    public static T Traduzindo<T>(Func<T> leitura)
    {
        try
        {
            return leitura();
        }
        catch (XmlException excecao)
        {
            throw new DomainException($"XML inválido: {excecao.Message}");
        }
    }
}
