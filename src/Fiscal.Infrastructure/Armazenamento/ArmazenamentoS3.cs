using Amazon.S3;
using Amazon.S3.Model;
using Fiscal.Application.Armazenamento;
using Microsoft.Extensions.Logging;

namespace Fiscal.Infrastructure.Armazenamento;

/// <summary>
/// Implementação sobre a API do S3. Em desenvolvimento aponta para o MinIO do
/// compose; em produção, para S3, R2 ou qualquer compatível — muda a configuração,
/// não o código.
/// </summary>
public sealed class ArmazenamentoS3(
    IAmazonS3 cliente,
    OpcoesDeArmazenamento opcoes,
    ILogger<ArmazenamentoS3> logger) : IArmazenamentoDeXml
{
    public async Task GravarAsync(
        string chave,
        ReadOnlyMemory<byte> conteudo,
        CancellationToken cancellationToken)
    {
        using var fluxo = new MemoryStream(conteudo.ToArray(), writable: false);

        // Sem verificar se já existe: a chave é o hash do conteúdo, então reescrever
        // grava exatamente os mesmos bytes. Um HEAD antes do PUT custaria uma ida à
        // rede para evitar uma operação que é inofensiva.
        await cliente.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = opcoes.Bucket,
                Key = chave,
                InputStream = fluxo,
                ContentType = "application/xml",
            },
            cancellationToken);
    }

    public async Task<byte[]?> LerAsync(string chave, CancellationToken cancellationToken)
    {
        try
        {
            using var resposta = await cliente.GetObjectAsync(
                opcoes.Bucket, chave, cancellationToken);

            using var destino = new MemoryStream();
            await resposta.ResponseStream.CopyToAsync(destino, cancellationToken);

            return destino.ToArray();
        }
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Objeto {Chave} não encontrado no bucket {Bucket}.", chave, opcoes.Bucket);

            return null;
        }
    }

    /// <summary>
    /// Cria o bucket se ainda não existir. Chamado no start para que subir a stack
    /// pela primeira vez não exija passo manual no MinIO.
    /// </summary>
    public static async Task GarantirBucketAsync(
        IAmazonS3 cliente,
        OpcoesDeArmazenamento opcoes,
        CancellationToken cancellationToken)
    {
        var existentes = await cliente.ListBucketsAsync(cancellationToken);

        if (existentes.Buckets?.Exists(b => b.BucketName == opcoes.Bucket) is true)
        {
            return;
        }

        await cliente.PutBucketAsync(new PutBucketRequest { BucketName = opcoes.Bucket }, cancellationToken);
    }
}
