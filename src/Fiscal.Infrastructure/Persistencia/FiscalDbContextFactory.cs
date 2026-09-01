using Fiscal.Application.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fiscal.Infrastructure.Persistencia;

/// <summary>
/// Usada apenas pelo <c>dotnet ef</c> para gerar migrations sem subir a aplicação.
/// O contexto de acesso é nulo aqui de propósito: geração de schema não tem
/// requisição HTTP e, portanto, não tem CNPJ autorizado.
/// </summary>
public sealed class FiscalDbContextFactory : IDesignTimeDbContextFactory<FiscalDbContext>
{
    public FiscalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FiscalDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=fiscal;Username=fiscal;Password=fiscal")
            .Options;

        return new FiscalDbContext(options, new ContextoDeDesign());
    }

    private sealed class ContextoDeDesign : IContextoAcesso
    {
        public string? CnpjAutorizado => null;
    }
}
