using Fiscal.Application.Documentos;
using Fiscal.Domain.Comum;

namespace Fiscal.Infrastructure.Xml;

/// <summary>
/// Escolhe o parser pelo elemento raiz do XML, lendo só o suficiente para
/// identificá-lo — não carrega o documento inteiro duas vezes.
/// <para>
/// Acrescentar CT-e ou NFS-e é registrar mais um <see cref="IParserDocumentoFiscal"/>
/// no contêiner. Esta classe não muda, nem o caso de uso de ingestão.
/// </para>
/// </summary>
public sealed class SeletorDeParser(IEnumerable<IParserDocumentoFiscal> parsers) : ISeletorDeParser
{
    public IParserDocumentoFiscal Selecionar(ReadOnlyMemory<byte> xml)
    {
        var (elemento, espacoDeNomes) = LerRaiz(xml);

        return parsers.FirstOrDefault(p => p.Reconhece(elemento, espacoDeNomes))
            ?? throw new DomainException(
                $"Nenhum leitor registrado reconhece o documento (raiz '{elemento}', namespace '{espacoDeNomes}').");
    }

    private static (string Elemento, string? Namespace) LerRaiz(ReadOnlyMemory<byte> xml) =>
        LeitorXmlSeguro.Traduzindo<(string, string?)>(() =>
        {
            using var fluxo = new MemoryStream(xml.ToArray(), writable: false);
            using var leitor = LeitorXmlSeguro.Criar(fluxo);

            // Avança até o primeiro elemento e para. O leitor já está blindado, então
            // até esta inspeção rasa é segura contra XXE — é aqui que um DOCTYPE
            // malicioso é recusado, antes de qualquer parser ver o documento.
            while (leitor.Read())
            {
                if (leitor.NodeType == System.Xml.XmlNodeType.Element)
                {
                    return (leitor.LocalName, leitor.NamespaceURI);
                }
            }

            throw new DomainException("Conteúdo enviado não é um XML válido.");
        });
}
