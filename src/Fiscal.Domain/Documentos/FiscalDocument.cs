using Fiscal.Domain.Comum;

namespace Fiscal.Domain.Documentos;

/// <summary>
/// Documento fiscal recebido. A modelagem separa deliberadamente duas naturezas:
///
///   1. O documento fiscal em si (chave, emitente, destinatário, valores, itens,
///      XML original) é IMUTÁVEL. Documento autorizado não se altera — corrige-se
///      por carta de correção ou cancela-se, ambos eventos que geram outro XML.
///      Por isso não existe setter público nem método que altere esses campos.
///
///   2. Os metadados de gestão (observação, tags, status de conciliação) nascem
///      do nosso processo, não do Fisco, e são livremente mutáveis.
///
/// O PUT da API opera exclusivamente sobre (2). A restrição é estrutural, não
/// convencional: não há caminho no código para sobrescrever (1).
/// </summary>
public sealed class FiscalDocument
{
    private readonly List<FiscalDocumentItem> _itens = [];

    private FiscalDocument()
    {
    }

    public Guid Id { get; private set; }

    // ---- Documento fiscal: imutável -------------------------------------

    public TipoDocumentoFiscal Tipo { get; private set; }

    /// <summary>Chave de acesso de 44 dígitos. É a identidade natural e a base da idempotência.</summary>
    public string ChaveAcesso { get; private set; } = string.Empty;

    public string Numero { get; private set; } = string.Empty;

    public string Serie { get; private set; } = string.Empty;

    public string CnpjEmitente { get; private set; } = string.Empty;

    public string NomeEmitente { get; private set; } = string.Empty;

    public string UfEmitente { get; private set; } = string.Empty;

    /// <summary>CPF ou CNPJ do destinatário. Dado pessoal quando CPF — ver <see cref="DadosSensiveis"/>.</summary>
    public string? DocumentoDestinatario { get; private set; }

    public string? NomeDestinatario { get; private set; }

    public DateTimeOffset DataEmissao { get; private set; }

    public decimal ValorTotal { get; private set; }

    /// <summary>
    /// SHA-256 do XML exatamente como recebido. Serve a dois propósitos: distinguir
    /// reenvio idêntico de reenvio divergente na ingestão, e servir de ETag no GET.
    /// </summary>
    public string HashConteudo { get; private set; } = string.Empty;

    /// <summary>
    /// XML original em bytes, não em string: o documento é prova legal e precisa
    /// sobreviver byte a byte, sem passar por normalização de encoding. Nunca é
    /// carregado na listagem — só no detalhe.
    /// </summary>
    public byte[] XmlBruto { get; private set; } = [];

    public IReadOnlyCollection<FiscalDocumentItem> Itens => _itens.AsReadOnly();

    // ---- Metadados de gestão: mutáveis ----------------------------------

    public string? Observacao { get; private set; }

    public string[] Tags { get; private set; } = [];

    public StatusConciliacao Status { get; private set; }

    // ---- Auditoria e exclusão lógica ------------------------------------

    public DateTimeOffset RecebidoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>
    /// Exclusão é sempre lógica. O Fisco exige guarda de 5 anos: apagar a linha
    /// seria descumprir obrigação legal, e é também a razão pela qual um pedido de
    /// eliminação sob a LGPD não se aplica aqui (base legal: obrigação legal).
    /// </summary>
    public bool Excluido { get; private set; }

    public DateTimeOffset? ExcluidoEm { get; private set; }

    public string? MotivoExclusao { get; private set; }

    public static FiscalDocument Registrar(
        TipoDocumentoFiscal tipo,
        string chaveAcesso,
        string numero,
        string serie,
        string cnpjEmitente,
        string nomeEmitente,
        string ufEmitente,
        string? documentoDestinatario,
        string? nomeDestinatario,
        DateTimeOffset dataEmissao,
        decimal valorTotal,
        string hashConteudo,
        byte[] xmlBruto,
        IEnumerable<FiscalDocumentItem> itens,
        DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(chaveAcesso))
        {
            throw new DomainException("Documento sem chave de acesso.");
        }

        if (string.IsNullOrWhiteSpace(cnpjEmitente))
        {
            throw new DomainException("Documento sem CNPJ do emitente.");
        }

        if (xmlBruto.Length == 0)
        {
            throw new DomainException("Documento sem conteúdo XML.");
        }

        var documento = new FiscalDocument
        {
            Id = Guid.CreateVersion7(),
            Tipo = tipo,
            ChaveAcesso = chaveAcesso,
            Numero = numero,
            Serie = serie,
            CnpjEmitente = cnpjEmitente,
            NomeEmitente = nomeEmitente,
            UfEmitente = ufEmitente,
            DocumentoDestinatario = documentoDestinatario,
            NomeDestinatario = nomeDestinatario,
            DataEmissao = dataEmissao,
            ValorTotal = valorTotal,
            HashConteudo = hashConteudo,
            XmlBruto = xmlBruto,
            Status = StatusConciliacao.Pendente,
            RecebidoEm = agora,
            AtualizadoEm = agora,
        };

        documento._itens.AddRange(itens);

        return documento;
    }

    /// <summary>Único caminho de mutação. Não toca em nenhum campo do documento fiscal.</summary>
    public void AtualizarMetadados(
        string? observacao,
        string[] tags,
        StatusConciliacao status,
        DateTimeOffset agora)
    {
        if (Excluido)
        {
            throw new DomainException("Documento excluído não aceita alteração de metadados.");
        }

        Observacao = observacao;
        Tags = tags;
        Status = status;
        AtualizadoEm = agora;
    }

    public void Excluir(string motivo, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("Exclusão exige motivo, para rastreabilidade da auditoria.");
        }

        if (Excluido)
        {
            return;
        }

        Excluido = true;
        ExcluidoEm = agora;
        MotivoExclusao = motivo;
        AtualizadoEm = agora;
    }
}
