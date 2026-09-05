using Fiscal.Domain.Lotes;

namespace Fiscal.Application.Lotes;

/// <summary>Um arquivo como chegou na requisição, antes de qualquer interpretação.</summary>
public sealed record ArquivoParaIngestao(string Nome, ReadOnlyMemory<byte> Conteudo);

/// <summary>
/// Mensagem que trafega na fila. Note o que NÃO vai nela: o XML. Vai a chave do
/// storage — broker não é meio de transporte de arquivo, e uma mensagem pequena
/// atravessa retry e reentrega sem custo.
/// </summary>
public sealed record ArquivoRecebido(
    Guid LoteId,
    Guid ItemId,
    string ChaveDeArmazenamento,
    string CnpjProprietario,
    string NomeArquivo);

public sealed record LoteAceito(Guid LoteId, int QuantidadeDeArquivos);

public sealed record SituacaoDoLote(
    Guid Id,
    string Situacao,
    DateTimeOffset RecebidoEm,
    DateTimeOffset AtualizadoEm,
    int Total,
    int Pendentes,
    int Ingeridos,
    int Duplicados,
    int Rejeitados,
    IReadOnlyList<SituacaoDoItem> Itens)
{
    public static SituacaoDoLote De(LoteDeIngestao lote) => new(
        lote.Id,
        lote.Situacao.ToString(),
        lote.RecebidoEm,
        lote.AtualizadoEm,
        lote.Itens.Count,
        lote.Itens.Count(i => i.Situacao is SituacaoItem.Pendente),
        lote.Itens.Count(i => i.Situacao is SituacaoItem.Ingerido),
        lote.Itens.Count(i => i.Situacao is SituacaoItem.Duplicado),
        lote.Itens.Count(i => i.Situacao is SituacaoItem.Rejeitado),
        [.. lote.Itens.OrderBy(i => i.NomeArquivo).Select(SituacaoDoItem.De)]);
}

public sealed record SituacaoDoItem(
    Guid Id,
    string NomeArquivo,
    string Situacao,
    string? Motivo,
    Guid? DocumentoId,
    DateTimeOffset? ProcessadoEm)
{
    public static SituacaoDoItem De(ItemDoLote item) => new(
        item.Id,
        item.NomeArquivo,
        item.Situacao.ToString(),
        item.Motivo,
        item.DocumentoId,
        item.ProcessadoEm);
}
