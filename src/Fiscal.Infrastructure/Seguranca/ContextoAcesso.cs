using Fiscal.Application.Seguranca;
using Microsoft.Extensions.Logging;

namespace Fiscal.Infrastructure.Seguranca;

/// <summary>
/// Implementação por escopo (uma instância por requisição HTTP ou por mensagem
/// consumida). Registrada como <see cref="IContextoAcesso"/> e como
/// <see cref="IDefinidorContextoAcesso"/> apontando para a MESMA instância.
/// </summary>
public sealed class ContextoAcesso(ILogger<ContextoAcesso> logger)
    : IContextoAcesso, IDefinidorContextoAcesso
{
    private string? _cnpj;
    private int _escoposDeSistemaAbertos;

    public string? CnpjAutorizado => _escoposDeSistemaAbertos > 0
        ? null
        : _cnpj ?? throw new AcessoNaoAutenticadoException();

    public void DefinirCnpj(string cnpj)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cnpj);
        _cnpj = cnpj;
    }

    public IDisposable AbrirEscopoDeSistema(string motivo)
    {
        _escoposDeSistemaAbertos++;

        // Acesso cross-tenant é sempre registrado. Se este log aparecer durante o
        // atendimento de uma requisição HTTP, algo está errado no middleware.
        logger.LogWarning("Escopo de sistema aberto (sem isolamento por CNPJ). Motivo: {Motivo}", motivo);

        return new EscopoDeSistema(this);
    }

    private sealed class EscopoDeSistema(ContextoAcesso contexto) : IDisposable
    {
        private bool _fechado;

        public void Dispose()
        {
            if (_fechado)
            {
                return;
            }

            _fechado = true;
            contexto._escoposDeSistemaAbertos--;
        }
    }
}
