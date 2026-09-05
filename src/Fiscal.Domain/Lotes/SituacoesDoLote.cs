namespace Fiscal.Domain.Lotes;

/// <summary>
/// Situação do lote. É <b>derivada</b> das situações dos itens, nunca definida
/// diretamente de fora — ver <see cref="LoteDeIngestao.Recalcular"/>.
/// </summary>
public enum SituacaoLote
{
    /// <summary>Arquivos gravados, nenhum item processado ainda.</summary>
    Recebido = 1,

    /// <summary>Pelo menos um item processado e pelo menos um ainda pendente.</summary>
    Processando = 2,

    /// <summary>Todos os itens terminaram, nenhum rejeitado.</summary>
    Concluido = 3,

    /// <summary>Todos os itens terminaram e pelo menos um foi rejeitado.</summary>
    ConcluidoComErros = 4,
}

public enum SituacaoItem
{
    /// <summary>Gravado no storage, aguardando o worker.</summary>
    Pendente = 1,

    /// <summary>Documento novo, persistido agora.</summary>
    Ingerido = 2,

    /// <summary>A chave de acesso já existia com o mesmo conteúdo. Não é erro.</summary>
    Duplicado = 3,

    /// <summary>XML inválido, emitente alheio, ou chave já existente com outro conteúdo.</summary>
    Rejeitado = 4,
}
