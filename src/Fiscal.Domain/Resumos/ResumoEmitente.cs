namespace Fiscal.Domain.Resumos;

/// <summary>
/// Agregado por emitente e competência (AAAA-MM), mantido pelo consumidor da fila.
/// É o "algo útil" do item 3 do enunciado, escolhido de propósito: por ser um
/// acumulador, ele quebra de forma visível se o consumidor não for idempotente —
/// então o mesmo recurso demonstra o item 3 e prova o item 7 no lado do consumo.
/// </summary>
public sealed class ResumoEmitente
{
    private ResumoEmitente()
    {
    }

    public Guid Id { get; private set; }

    public string CnpjEmitente { get; private set; } = string.Empty;

    /// <summary>Competência no formato AAAA-MM, derivada da data de emissão.</summary>
    public string Competencia { get; private set; } = string.Empty;

    public int QuantidadeDocumentos { get; private set; }

    public decimal ValorTotal { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public static ResumoEmitente Criar(string cnpjEmitente, string competencia, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(),
        CnpjEmitente = cnpjEmitente,
        Competencia = competencia,
        QuantidadeDocumentos = 0,
        ValorTotal = 0m,
        AtualizadoEm = agora,
    };

    /// <summary>
    /// Acumula um documento. A proteção contra dupla contagem NÃO está aqui — está
    /// na tabela de mensagens processadas, na mesma transação. Ver o consumidor.
    /// </summary>
    public void Acumular(decimal valorDocumento, DateTimeOffset agora)
    {
        QuantidadeDocumentos++;
        ValorTotal += valorDocumento;
        AtualizadoEm = agora;
    }
}
