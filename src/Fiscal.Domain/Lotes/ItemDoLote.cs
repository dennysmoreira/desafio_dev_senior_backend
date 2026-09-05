using Fiscal.Domain.Comum;

namespace Fiscal.Domain.Lotes;

/// <summary>
/// Um arquivo dentro do lote. Nasce <see cref="SituacaoItem.Pendente"/> e transiciona
/// exatamente uma vez, quando o worker o processa.
/// </summary>
public sealed class ItemDoLote
{
    private ItemDoLote()
    {
    }

    public Guid Id { get; private set; }

    public Guid LoteId { get; private set; }

    public string NomeArquivo { get; private set; } = string.Empty;

    /// <summary>Chave no storage, que é o SHA-256 do conteúdo.</summary>
    public string ChaveDeArmazenamento { get; private set; } = string.Empty;

    public int TamanhoEmBytes { get; private set; }

    public SituacaoItem Situacao { get; private set; }

    /// <summary>Preenchido apenas em <see cref="SituacaoItem.Rejeitado"/>.</summary>
    public string? Motivo { get; private set; }

    /// <summary>Documento resultante, quando ingerido ou reconhecido como duplicado.</summary>
    public Guid? DocumentoId { get; private set; }

    public DateTimeOffset? ProcessadoEm { get; private set; }

    public static ItemDoLote Criar(string nomeArquivo, string chaveDeArmazenamento, int tamanhoEmBytes) => new()
    {
        Id = Guid.CreateVersion7(),
        NomeArquivo = nomeArquivo,
        ChaveDeArmazenamento = chaveDeArmazenamento,
        TamanhoEmBytes = tamanhoEmBytes,
        Situacao = SituacaoItem.Pendente,
    };

    public void MarcarIngerido(Guid documentoId, DateTimeOffset agora) =>
        Concluir(SituacaoItem.Ingerido, documentoId, motivo: null, agora);

    public void MarcarDuplicado(Guid documentoId, DateTimeOffset agora) =>
        Concluir(SituacaoItem.Duplicado, documentoId, motivo: null, agora);

    public void MarcarRejeitado(string motivo, DateTimeOffset agora) =>
        Concluir(SituacaoItem.Rejeitado, documentoId: null, motivo, agora);

    /// <summary>
    /// Transição única. Reprocessar o mesmo item é no-op em vez de exceção: o
    /// consumidor é at-least-once, então a mesma mensagem pode chegar duas vezes, e
    /// o segundo processamento precisa ser inofensivo — não um erro que iria para a
    /// fila venenosa.
    /// </summary>
    private void Concluir(SituacaoItem situacao, Guid? documentoId, string? motivo, DateTimeOffset agora)
    {
        if (Situacao is not SituacaoItem.Pendente)
        {
            return;
        }

        if (situacao is SituacaoItem.Rejeitado && string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("Rejeição exige motivo, senão o cliente não sabe o que corrigir.");
        }

        Situacao = situacao;
        DocumentoId = documentoId;
        Motivo = motivo;
        ProcessadoEm = agora;
    }
}
