using Fiscal.Application.Documentos;
using Fiscal.Application.Resumos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fiscal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddFiscalApplication(this IServiceCollection services)
    {
        // TimeProvider do próprio BCL em vez de uma IClock caseira: os testes trocam
        // por FakeTimeProvider sem que o domínio precise conhecer abstração nossa.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<RegistrarDocumento>();
        services.AddScoped<ConsultarDocumentos>();
        services.AddScoped<ObterDocumento>();
        services.AddScoped<AtualizarObservacaoDocumento>();
        services.AddScoped<ExcluirDocumento>();
        services.AddScoped<AtualizarResumoDoEmitente>();
        services.AddScoped<ConsultarResumos>();

        return services;
    }
}
