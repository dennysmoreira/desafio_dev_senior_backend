using System.Security.Cryptography;
using System.Text;
using Fiscal.Application.Seguranca;

namespace Fiscal.Api.Seguranca;

/// <summary>
/// Autenticação deliberadamente simples: um cabeçalho <c>X-Cnpj</c> identificando o
/// contribuinte e um <c>X-Api-Key</c> conferido contra a configuração.
/// <para>
/// Em produção isto seria um JWT com o CNPJ numa claim. A escolha é consciente: o
/// que está sendo demonstrado é o <b>isolamento</b>, que vive no filtro global do
/// <c>DbContext</c> e não muda em nada com a troca do mecanismo de autenticação.
/// Gastar o orçamento montando emissão de token não melhoraria a parte que importa.
/// </para>
/// </summary>
public sealed class AutenticacaoPorCnpj(RequestDelegate proximo, string chaveEsperada)
{
    private const string CabecalhoCnpj = "X-Cnpj";
    private const string CabecalhoChave = "X-Api-Key";

    private static readonly string[] RotasPublicas = ["/health", "/openapi", "/scalar"];

    public async Task InvokeAsync(HttpContext contexto, IDefinidorContextoAcesso definidor)
    {
        var caminho = contexto.Request.Path.Value ?? string.Empty;

        if (RotasPublicas.Any(rota => caminho.StartsWith(rota, StringComparison.OrdinalIgnoreCase)))
        {
            await proximo(contexto);
            return;
        }

        var chave = contexto.Request.Headers[CabecalhoChave].ToString();

        // Comparação de tempo constante: comparar segredo com == vaza informação pelo
        // tempo de resposta e permite descobrir a chave caractere a caractere.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(chave),
                Encoding.UTF8.GetBytes(chaveEsperada)))
        {
            await RecusarAsync(contexto, "Chave de API ausente ou inválida.");
            return;
        }

        var cnpj = new string(contexto.Request.Headers[CabecalhoCnpj].ToString().Where(char.IsAsciiDigit).ToArray());

        if (cnpj.Length != 14)
        {
            await RecusarAsync(contexto, $"Cabeçalho {CabecalhoCnpj} ausente ou fora do formato (14 dígitos).");
            return;
        }

        definidor.DefinirCnpj(cnpj);

        await proximo(contexto);
    }

    private static async Task RecusarAsync(HttpContext contexto, string detalhe)
    {
        contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;

        await contexto.Response.WriteAsJsonAsync(new
        {
            title = "Não autenticado",
            status = StatusCodes.Status401Unauthorized,
            detail = detalhe,
        });
    }
}
