using System.Security.Cryptography;

namespace Fiscal.Domain.Comum;

/// <summary>
/// Identidade de conteúdo do XML. É calculado sobre os bytes exatamente como
/// chegaram — nada de normalizar, reindentar ou reserializar antes, senão dois
/// arquivos idênticos para o Fisco passariam a ter hashes diferentes aqui.
/// </summary>
public static class HashConteudo
{
    public static string Calcular(ReadOnlySpan<byte> conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(conteudo));
}
