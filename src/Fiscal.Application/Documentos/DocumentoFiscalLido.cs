using Fiscal.Domain.Documentos;

namespace Fiscal.Application.Documentos;

/// <summary>
/// Resultado da leitura de um XML fiscal, antes de virar entidade. É um DTO de
/// fronteira: o parser não conhece <see cref="DocumentoFiscal"/> nem persistência.
/// </summary>
public sealed record DocumentoFiscalLido(
    TipoDocumentoFiscal Tipo,
    string ChaveAcesso,
    string Numero,
    string Serie,
    string CnpjEmitente,
    string NomeEmitente,
    string UfEmitente,
    string? DocumentoDestinatario,
    string? NomeDestinatario,
    DateTimeOffset DataEmissao,
    decimal ValorTotal,
    IReadOnlyList<ItemLido> Itens);

public sealed record ItemLido(
    int Numero,
    string Codigo,
    string Descricao,
    string? Ncm,
    string? Cfop,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);
