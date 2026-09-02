using System.Globalization;
using System.Xml.Linq;
using Fiscal.Application.Documentos;
using Fiscal.Domain.Comum;
using Fiscal.Domain.Documentos;

namespace Fiscal.Infrastructure.Xml;

/// <summary>
/// Leitor de NF-e (layout 4.00). Aceita tanto o XML de distribuição
/// (<c>nfeProc</c>, já com o protocolo de autorização) quanto a <c>NFe</c> avulsa.
/// <para>
/// Extrai apenas o cabeçalho e os itens: <c>ide</c>, <c>emit</c>, <c>dest</c>,
/// <c>total</c> e <c>det</c>. Os outros ~450 campos do layout continuam no XML
/// bruto, que é guardado íntegro — mapear tudo seria semanas de trabalho e nenhum
/// consumidor da API pediu.
/// </para>
/// </summary>
public sealed class ParserNfe : IParserDocumentoFiscal
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    public bool Reconhece(string elementoRaiz, string? namespaceRaiz) =>
        namespaceRaiz == Nfe.NamespaceName
        && elementoRaiz is "nfeProc" or "NFe";

    public DocumentoFiscalLido Ler(ReadOnlyMemory<byte> xml)
    {
        var documento = LeitorXmlSeguro.Traduzindo(() =>
        {
            using var fluxo = new MemoryStream(xml.ToArray(), writable: false);
            using var leitor = LeitorXmlSeguro.Criar(fluxo);

            return XDocument.Load(leitor);
        });

        var infNfe = documento.Descendants(Nfe + "infNFe").FirstOrDefault()
            ?? throw new DomainException("XML de NF-e sem elemento infNFe.");

        var ide = Obrigatorio(infNfe, "ide");
        var emit = Obrigatorio(infNfe, "emit");
        var dest = infNfe.Element(Nfe + "dest");

        return new DocumentoFiscalLido(
            Tipo: TipoDocumentoFiscal.Nfe,
            ChaveAcesso: ExtrairChave(infNfe),
            Numero: Texto(ide, "nNF") ?? throw new DomainException("NF-e sem número (nNF)."),
            Serie: Texto(ide, "serie") ?? "0",
            CnpjEmitente: Texto(emit, "CNPJ") ?? throw new DomainException("NF-e sem CNPJ do emitente."),
            NomeEmitente: Texto(emit, "xNome") ?? string.Empty,
            UfEmitente: Texto(emit.Element(Nfe + "enderEmit"), "UF") ?? string.Empty,
            DocumentoDestinatario: Texto(dest, "CNPJ") ?? Texto(dest, "CPF"),
            NomeDestinatario: Texto(dest, "xNome"),
            DataEmissao: ExtrairDataEmissao(ide),
            ValorTotal: ExtrairValorTotal(infNfe),
            Itens: [.. infNfe.Elements(Nfe + "det").Select(LerItem)]);
    }

    /// <summary>
    /// A chave de acesso vive no atributo <c>Id</c> do <c>infNFe</c>, prefixada por
    /// "NFe". São 44 dígitos — validamos o formato porque é a identidade natural do
    /// documento e a âncora da idempotência: aceitar uma chave malformada aqui
    /// significa aceitar duplicata depois.
    /// </summary>
    private static string ExtrairChave(XElement infNfe)
    {
        var bruto = infNfe.Attribute("Id")?.Value
            ?? throw new DomainException("NF-e sem atributo Id no infNFe.");

        var chave = bruto.StartsWith("NFe", StringComparison.OrdinalIgnoreCase)
            ? bruto[3..]
            : bruto;

        if (chave.Length != 44 || !chave.All(char.IsAsciiDigit))
        {
            throw new DomainException($"Chave de acesso inválida: esperados 44 dígitos, veio '{bruto}'.");
        }

        return chave;
    }

    /// <summary>
    /// 4.00 usa dhEmi com fuso (ex.: -03:00); 3.10 usava dEmi só com a data.
    /// <para>
    /// Convertido para UTC porque <c>timestamptz</c> do PostgreSQL guarda um
    /// instante, não um deslocamento — o Npgsql recusa DateTimeOffset com offset
    /// diferente de zero. Nada se perde: o horário local original continua no XML
    /// bruto, que é a fonte de verdade legal.
    /// </para>
    /// </summary>
    private static DateTimeOffset ExtrairDataEmissao(XElement ide)
    {
        var comFuso = Texto(ide, "dhEmi");

        if (comFuso is not null)
        {
            return DateTimeOffset.Parse(comFuso, CultureInfo.InvariantCulture).ToUniversalTime();
        }

        var soData = Texto(ide, "dEmi")
            ?? throw new DomainException("NF-e sem data de emissão (dhEmi ou dEmi).");

        return new DateTimeOffset(DateTime.Parse(soData, CultureInfo.InvariantCulture), TimeSpan.Zero);
    }

    private static decimal ExtrairValorTotal(XElement infNfe)
    {
        var icmsTot = infNfe.Element(Nfe + "total")?.Element(Nfe + "ICMSTot");

        return Decimal(icmsTot, "vNF")
            ?? throw new DomainException("NF-e sem valor total (total/ICMSTot/vNF).");
    }

    private static ItemLido LerItem(XElement det)
    {
        var prod = det.Element(Nfe + "prod")
            ?? throw new DomainException("Item de NF-e sem elemento prod.");

        return new ItemLido(
            Numero: int.TryParse(det.Attribute("nItem")?.Value, out var numero) ? numero : 0,
            Codigo: Texto(prod, "cProd") ?? string.Empty,
            Descricao: Texto(prod, "xProd") ?? string.Empty,
            Ncm: Texto(prod, "NCM"),
            Cfop: Texto(prod, "CFOP"),
            Quantidade: Decimal(prod, "qCom") ?? 0m,
            ValorUnitario: Decimal(prod, "vUnCom") ?? 0m,
            ValorTotal: Decimal(prod, "vProd") ?? 0m);
    }

    private static XElement Obrigatorio(XElement pai, string nome) =>
        pai.Element(Nfe + nome) ?? throw new DomainException($"XML de NF-e sem elemento {nome}.");

    private static string? Texto(XElement? pai, string nome) => pai?.Element(Nfe + nome)?.Value;

    private static decimal? Decimal(XElement? pai, string nome) =>
        decimal.TryParse(Texto(pai, nome), NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : null;
}
