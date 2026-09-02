namespace Fiscal.Domain.Comum;

/// <summary>
/// Mascaramento de identificadores de pessoa. Um XML fiscal carrega CPF, nome e
/// endereço do destinatário — dado pessoal sob a LGPD. O valor íntegro só sai no
/// endpoint de detalhe, para quem está autorizado naquele CNPJ; log e listagem
/// recebem sempre a versão mascarada.
/// </summary>
public static class DadosSensiveis
{
    /// <summary>CPF 12345678901 vira ***456789**; CNPJ 12345678000199 vira **345678******.</summary>
    public static string Mascarar(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
        {
            return string.Empty;
        }

        var digitos = new string(documento.Where(char.IsAsciiDigit).ToArray());

        return digitos.Length switch
        {
            11 => $"***{digitos[3..9]}**",
            14 => $"**{digitos[2..8]}******",
            _ => new string('*', digitos.Length),
        };
    }

    /// <summary>Mantém o primeiro nome e mascara o restante, para log e listagem.</summary>
    public static string MascararNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            return string.Empty;
        }

        var partes = nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return partes.Length == 1
            ? partes[0]
            : $"{partes[0]} {string.Join(' ', partes.Skip(1).Select(p => new string('*', p.Length)))}";
    }
}
