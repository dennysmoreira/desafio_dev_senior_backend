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
}
