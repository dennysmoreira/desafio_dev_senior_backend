using Fiscal.Application.Documentos;
using Fiscal.Application.Lotes;
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

        // Escrita
        services.AddScoped<ReceberLote>();
        services.AddScoped<IngerirArquivo>();
        services.AddScoped<AtualizarObservacaoDocumento>();
        services.AddScoped<ExcluirDocumento>();

        // Leitura
        services.AddScoped<ConsultarLote>();
        services.AddScoped<ListarLotes>();
        services.AddScoped<ConsultarDocumentos>();
        services.AddScoped<ObterDocumento>();
        services.AddScoped<ConsultarResumos>();

        return services;
    }
}
