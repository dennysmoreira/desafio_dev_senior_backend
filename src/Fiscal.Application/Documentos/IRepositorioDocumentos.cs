using Fiscal.Domain.Documentos;

namespace Fiscal.Application.Documentos;

public interface IRepositorioDocumentos
{
    /// <summary>
    /// Tenta inserir. Devolve <see langword="false"/> quando o índice único
    /// <c>ux_documento_fiscal_tipo_chave</c> recusa a linha porque a chave já existe.
    /// <para>
    /// A tradução da violação de unicidade acontece na infraestrutura, que é a única
    /// camada que conhece o código de erro do Postgres. Não existe SELECT antes do
    /// INSERT: o banco é a autoridade e não há janela entre checar e gravar.
    /// </para>
    /// </summary>
    Task<bool> TentarInserirAsync(DocumentoFiscal documento, CancellationToken cancellationToken);

    Task<DocumentoFiscal?> ObterPorChaveAsync(
        TipoDocumentoFiscal tipo,
        string chaveAcesso,
        CancellationToken cancellationToken);

    Task<PaginaDe<ResumoDocumento>> ListarAsync(FiltroDocumentos filtro, CancellationToken cancellationToken);

    /// <summary>
    /// Documento por id, sem rastreamento. Traz também o excluído logicamente, para
    /// que o caso de uso possa distinguir "nunca existiu" de "foi removido" — 404 e
    /// 410 dizem coisas diferentes ao cliente. O isolamento por CNPJ continua valendo.
    /// </summary>
    Task<DocumentoFiscal?> ObterParaLeituraAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Mesma regra, mas com rastreamento, para PUT e DELETE.</summary>
    Task<DocumentoFiscal?> ObterParaEdicaoAsync(Guid id, CancellationToken cancellationToken);

    Task SalvarAsync(CancellationToken cancellationToken);
}
