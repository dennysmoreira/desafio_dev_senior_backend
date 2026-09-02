using System.Globalization;
using System.Text;

namespace Fiscal.UnitTests.Recursos;

/// <summary>
/// Gerador de NF-e para os testes. Existe para que cada teste declare só o que
/// importa para ele — a chave, o emitente, o valor — em vez de carregar 60 linhas
/// de XML que escondem a variável sob teste.
/// </summary>
public static class NfeDeTeste
{
    public const string CnpjEmitentePadrao = "12345678000199";

    public const string CpfDestinatarioPadrao = "52998224725";

    /// <summary>
    /// Monta uma chave de acesso válida em formato: cUF(2) AAMM(4) CNPJ(14) mod(2)
    /// serie(3) nNF(9) tpEmis(1) cNF(8) DV(1) = 44 dígitos.
    /// </summary>
    public static string Chave(int sequencial = 1, string? cnpjEmitente = null)
    {
        var chave = "35" + "2601" + (cnpjEmitente ?? CnpjEmitentePadrao) + "55" + "001"
            + sequencial.ToString("D9", CultureInfo.InvariantCulture) + "1"
            + sequencial.ToString("D8", CultureInfo.InvariantCulture) + "0";

        return chave.Length == 44
            ? chave
            : throw new InvalidOperationException($"Chave de teste com {chave.Length} dígitos, esperados 44.");
    }

    public static byte[] Bytes(
        string? chave = null,
        string cnpjEmitente = CnpjEmitentePadrao,
        decimal valorTotal = 300.00m,
        string? cpfDestinatario = CpfDestinatarioPadrao,
        string dataEmissao = "2026-01-15T10:30:00-03:00") =>
        Encoding.UTF8.GetBytes(Texto(chave, cnpjEmitente, valorTotal, cpfDestinatario, dataEmissao));

    public static string Texto(
        string? chave = null,
        string cnpjEmitente = CnpjEmitentePadrao,
        decimal valorTotal = 300.00m,
        string? cpfDestinatario = CpfDestinatarioPadrao,
        string dataEmissao = "2026-01-15T10:30:00-03:00")
    {
        var destinatario = cpfDestinatario is null
            ? string.Empty
            : $"<dest><CPF>{cpfDestinatario}</CPF><xNome>Maria Aparecida de Souza</xNome></dest>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe>
                <infNFe Id="NFe{chave ?? Chave()}" versao="4.00">
                  <ide>
                    <cUF>35</cUF><mod>55</mod><serie>1</serie><nNF>1</nNF>
                    <dhEmi>{dataEmissao}</dhEmi><tpNF>1</tpNF>
                  </ide>
                  <emit>
                    <CNPJ>{cnpjEmitente}</CNPJ>
                    <xNome>Comercio Exemplo Ltda</xNome>
                    <enderEmit><UF>SP</UF></enderEmit>
                  </emit>
                  {destinatario}
                  <det nItem="1">
                    <prod>
                      <cProd>SKU-001</cProd><xProd>Caderno universitario</xProd>
                      <NCM>48201000</NCM><CFOP>5102</CFOP>
                      <qCom>10.0000</qCom><vUnCom>15.5000</vUnCom><vProd>155.00</vProd>
                    </prod>
                  </det>
                  <det nItem="2">
                    <prod>
                      <cProd>SKU-002</cProd><xProd>Caneta esferografica</xProd>
                      <NCM>96081000</NCM><CFOP>5102</CFOP>
                      <qCom>50.0000</qCom><vUnCom>2.9000</vUnCom><vProd>145.00</vProd>
                    </prod>
                  </det>
                  <total>
                    <ICMSTot><vNF>{valorTotal.ToString("F2", CultureInfo.InvariantCulture)}</vNF></ICMSTot>
                  </total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    /// <summary>NF-e com DOCTYPE declarando entidade externa — o ataque XXE clássico.</summary>
    public static byte[] ComAtaqueXxe() => Encoding.UTF8.GetBytes(
        """
        <?xml version="1.0"?>
        <!DOCTYPE raiz [<!ENTITY vazamento SYSTEM "file:///etc/passwd">]>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><x>&vazamento;</x></nfeProc>
        """);

    /// <summary>Entidades recursivas: o "billion laughs".</summary>
    public static byte[] ComBombaDeEntidades() => Encoding.UTF8.GetBytes(
        """
        <?xml version="1.0"?>
        <!DOCTYPE raiz [
          <!ENTITY a "aaaaaaaaaa">
          <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
          <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
        ]>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><x>&c;</x></nfeProc>
        """);
}
