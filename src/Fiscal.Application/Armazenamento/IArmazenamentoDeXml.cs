namespace Fiscal.Application.Armazenamento;

/// <summary>
/// Guarda o XML original fora do banco.
/// <para>
/// A chave é sempre o SHA-256 do conteúdo, e isso resolve a idempotência do
/// armazenamento de graça: gravar duas vezes o mesmo arquivo escreve o mesmo objeto,
/// nunca duplica, e não há o que reconciliar depois. Também amarra o storage ao
/// mesmo mecanismo de identidade usado na ingestão.
/// </para>
/// <para>
/// A interface existe para que a aplicação não conheça S3, MinIO nem sistema de
/// arquivos — trocar de provedor é uma implementação nova e uma linha de registro.
/// </para>
/// </summary>
public interface IArmazenamentoDeXml
{
    Task GravarAsync(string chave, ReadOnlyMemory<byte> conteudo, CancellationToken cancellationToken);

    /// <summary>Devolve <see langword="null"/> quando o objeto não existe.</summary>
    Task<byte[]?> LerAsync(string chave, CancellationToken cancellationToken);
}
