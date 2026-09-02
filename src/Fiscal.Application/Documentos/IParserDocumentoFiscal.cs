namespace Fiscal.Application.Documentos;

/// <summary>
/// Leitor de um layout fiscal específico. Acrescentar CTe ou NFSe é escrever mais
/// uma implementação e registrá-la no contêiner — nenhuma linha do pipeline de
/// ingestão muda. Ver o teste que registra um parser falso e comprova isso.
/// </summary>
public interface IParserDocumentoFiscal
{
    /// <summary>Nome do elemento raiz que este parser reconhece, já sem namespace.</summary>
    bool Reconhece(string elementoRaiz, string? namespaceRaiz);

    DocumentoFiscalLido Ler(ReadOnlyMemory<byte> xml);
}

/// <summary>Seleciona o parser certo para um XML. Implementado na infraestrutura.</summary>
public interface ISeletorDeParser
{
    IParserDocumentoFiscal Selecionar(ReadOnlyMemory<byte> xml);
}
