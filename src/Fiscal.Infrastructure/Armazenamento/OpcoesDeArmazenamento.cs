namespace Fiscal.Infrastructure.Armazenamento;

public sealed class OpcoesDeArmazenamento
{
    public required string Endpoint { get; init; }

    public required string AccessKey { get; init; }

    public required string SecretKey { get; init; }

    public string Bucket { get; init; } = "documentos-fiscais";
}
