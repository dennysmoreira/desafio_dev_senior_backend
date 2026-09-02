using Fiscal.Application;
using Fiscal.Application.Documentos;
using Fiscal.Application.Mensageria;
using Fiscal.Application.Resumos;
using Fiscal.Application.Seguranca;
using Fiscal.Infrastructure.Mensageria;
using Fiscal.Infrastructure.Persistencia;
using Fiscal.Infrastructure.Seguranca;
using Fiscal.Infrastructure.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fiscal.Infrastructure;

/// <summary>
/// Ponto único de composição da infraestrutura. Existe para que
/// <c>Fiscal.Api</c> não precise referenciar tipos do EF Core nem do RabbitMQ —
/// a regra verificada em <c>Fiscal.ArchitectureTests</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFiscalInfrastructure(
        this IServiceCollection services,
        string stringDeConexao)
    {
        // AddDbContext e não AddDbContextPool: o contexto recebe IContextoAcesso por
        // construtor, que é um serviço com escopo de requisição. Instância poolada é
        // reaproveitada entre requisições e carregaria o CNPJ da requisição anterior
        // — trocar isolamento de tenant por microssegundos seria péssimo negócio.
        services.AddDbContext<FiscalDbContext>(opcoes => opcoes.UseNpgsql(stringDeConexao));

        // A mesma instância atende leitura e escrita do contexto de acesso, mas as
        // duas faces são registradas separadamente para que código de consulta
        // receba apenas a de leitura.
        services.AddScoped<ContextoAcesso>();
        services.AddScoped<IContextoAcesso>(sp => sp.GetRequiredService<ContextoAcesso>());
        services.AddScoped<IDefinidorContextoAcesso>(sp => sp.GetRequiredService<ContextoAcesso>());

        services.AddScoped<IRepositorioDocumentos, RepositorioDocumentos>();

        // Cada layout fiscal é mais uma linha aqui. Hoje só NF-e.
        services.AddScoped<IParserDocumentoFiscal, ParserNfe>();
        services.AddScoped<ISeletorDeParser, SeletorDeParser>();

        services.AddScoped<IRepositorioResumos, RepositorioResumos>();

        services.AddFiscalApplication();

        return services;
    }

    /// <summary>
    /// Mensageria. Separada do resto porque os testes de ingestão não precisam de
    /// broker: eles registram um publicador em log e continuam válidos.
    /// </summary>
    public static IServiceCollection AddFiscalMensageria(
        this IServiceCollection services,
        OpcoesRabbitMq opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<ConexaoRabbitMq>();
        services.AddScoped<IPublicadorEventos, PublicadorRabbitMq>();
        services.AddHostedService<ConsumidorResumo>();

        return services;
    }

    /// <summary>Publicador que só registra em log, para rodar sem broker.</summary>
    public static IServiceCollection AddFiscalMensageriaEmLog(this IServiceCollection services)
    {
        services.AddScoped<IPublicadorEventos, PublicadorEmLog>();

        return services;
    }

    /// <summary>
    /// Aplica migrations pendentes. Chamado no start da API para que o avaliador
    /// suba tudo com um comando. Nada de EnsureCreated: o schema versionado é o
    /// mesmo em qualquer ambiente.
    /// </summary>
    public static async Task MigrarBancoAsync(this IServiceProvider provedor, CancellationToken cancellationToken)
    {
        using var escopo = provedor.CreateScope();

        var definidor = escopo.ServiceProvider.GetRequiredService<IDefinidorContextoAcesso>();
        using var _ = definidor.AbrirEscopoDeSistema("aplicação de migrations no start");

        var db = escopo.ServiceProvider.GetRequiredService<FiscalDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
