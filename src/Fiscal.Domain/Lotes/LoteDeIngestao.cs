using Fiscal.Domain.Comum;

namespace Fiscal.Domain.Lotes;

/// <summary>
/// Um envio. Agrupa de 1 a <see cref="MaximoDeArquivos"/> XMLs recebidos numa mesma
/// requisição e serve de unidade de acompanhamento para quem enviou.
/// <para>
/// O lote não tem lógica de negócio fiscal — quem tem é <c>DocumentoFiscal</c>. Ele
/// existe para responder "o que aconteceu com o que eu mandei", que é a pergunta que
/// a ingestão assíncrona cria e a síncrona não tinha.
/// </para>
/// </summary>
public sealed class LoteDeIngestao
{
    /// <summary>
    /// Teto de arquivos por lote. Existe por dois motivos: limitar o trabalho de uma
    /// única requisição, e manter o agregado pequeno o bastante para ser carregado
    /// inteiro quando o worker recalcula a situação.
    /// </summary>
    public const int MaximoDeArquivos = 100;

    private readonly List<ItemDoLote> _itens = [];

    private LoteDeIngestao()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>CNPJ autenticado que enviou. É por ele que o filtro global isola.</summary>
    public string CnpjProprietario { get; private set; } = string.Empty;

    public SituacaoLote Situacao { get; private set; }

    public DateTimeOffset RecebidoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public IReadOnlyCollection<ItemDoLote> Itens => _itens.AsReadOnly();

    public static LoteDeIngestao Registrar(string cnpjProprietario, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(cnpjProprietario))
        {
            throw new DomainException("Lote sem CNPJ proprietário.");
        }

        return new LoteDeIngestao
        {
            Id = Guid.CreateVersion7(),
            CnpjProprietario = cnpjProprietario,
            Situacao = SituacaoLote.Recebido,
            RecebidoEm = agora,
            AtualizadoEm = agora,
        };
    }

    public ItemDoLote Adicionar(string nomeArquivo, string chaveDeArmazenamento, int tamanhoEmBytes)
    {
        if (_itens.Count >= MaximoDeArquivos)
        {
            throw new DomainException($"Lote aceita no máximo {MaximoDeArquivos} arquivos.");
        }

        var item = ItemDoLote.Criar(nomeArquivo, chaveDeArmazenamento, tamanhoEmBytes);

        _itens.Add(item);

        return item;
    }

    /// <summary>
    /// Deriva a situação a partir dos itens. Não existe setter: o estado do lote é
    /// consequência, nunca uma afirmação de quem chama. Chamado a cada item
    /// processado, e é idempotente — recalcular com os mesmos itens dá o mesmo
    /// resultado, o que importa porque o consumidor é at-least-once.
    /// </summary>
    public void Recalcular(DateTimeOffset agora)
    {
        if (_itens.Count == 0)
        {
            throw new DomainException("Lote sem itens não deveria existir.");
        }

        var pendentes = _itens.Count(i => i.Situacao is SituacaoItem.Pendente);
        var rejeitados = _itens.Count(i => i.Situacao is SituacaoItem.Rejeitado);

        if (pendentes == _itens.Count)
        {
            Situacao = SituacaoLote.Recebido;
        }
        else if (pendentes > 0)
        {
            Situacao = SituacaoLote.Processando;
        }
        else
        {
            Situacao = rejeitados > 0 ? SituacaoLote.ConcluidoComErros : SituacaoLote.Concluido;
        }

        AtualizadoEm = agora;
    }
}
