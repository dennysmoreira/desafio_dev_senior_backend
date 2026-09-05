using Amazon.S3;
using Fiscal.Application;
using Fiscal.Application.Armazenamento;
using Fiscal.Application.Documentos;
using Fiscal.Application.Mensageria;
using Fiscal.Application.Resumos;
using Fiscal.Application.Seguranca;
using Fiscal.Infrastructure.Armazenamento;
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
    /// Armazenamento do XML original. Chamado pelos dois deployáveis: a API grava, o
    /// worker lê. O cliente é singleton porque mantém pool de conexões HTTP.
    /// </summary>
    public static IServiceCollection AddArmazenamentoDeXml(
        this IServiceCollection services,
        OpcoesDeArmazenamento opcoes)
    {
        services.AddSingleton(opcoes);

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            opcoes.AccessKey,
            opcoes.SecretKey,
            new AmazonS3Config
            {
                ServiceURL = opcoes.Endpoint,

                // Obrigatório para MinIO e qualquer S3 self-hosted: sem isto o SDK
                // monta a URL como bucket.host, que só funciona na AWS.
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            }));

        services.AddSingleton<IArmazenamentoDeXml, ArmazenamentoS3>();

        return services;
    }

    /// <summary>Cria o bucket se não existir, para não haver passo manual no start.</summary>
    public static async Task PrepararArmazenamentoAsync(
        this IServiceProvider provedor,
        CancellationToken cancellationToken)
    {
        var cliente = provedor.GetRequiredService<IAmazonS3>();
        var opcoes = provedor.GetRequiredService<OpcoesDeArmazenamento>();

        await ArmazenamentoS3.GarantirBucketAsync(cliente, opcoes, cancellationToken);
    }

    /// <summary>
    /// Conexão com o broker. Chamada pelos dois deployáveis; a conexão é única por
    /// processo e as sessões saem dela.
    /// </summary>
    private static IServiceCollection AddConexaoRabbitMq(this IServiceCollection services, OpcoesRabbitMq opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<ConexaoRabbitMq>();

        return services;
    }

    /// <summary>
    /// Lado de escrita da mensageria — o que a API precisa.
    /// <para>
    /// Publicar e consumir são registrados separadamente de propósito. Quando os dois
    /// vinham no mesmo método, hospedar a API implicava hospedar o consumidor sem
    /// escolher: subir três réplicas para aguentar ingestão dava três consumidores de
    /// brinde, e um deploy rolling da API interrompia mensagem em processamento.
    /// </para>
    /// </summary>
    public static IServiceCollection AddPublicadorRabbitMq(
        this IServiceCollection services,
        OpcoesRabbitMq opcoes)
    {
        services.AddConexaoRabbitMq(opcoes);
        services.AddScoped<IPublicadorEventos, PublicadorRabbitMq>();

        return services;
    }

    /// <summary>
    /// Lado de leitura da mensageria — o que o worker precisa. Nenhum processo web
    /// registra isto.
    /// </summary>
    public static IServiceCollection AddConsumidorDeResumo(
        this IServiceCollection services,
        OpcoesRabbitMq opcoes)
    {
        services.AddConexaoRabbitMq(opcoes);
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
    /// Aplica migrations pendentes. Chamado no start dos dois deployáveis para que o
    /// avaliador suba tudo com um comando. Nada de EnsureCreated: o schema versionado
    /// é o mesmo em qualquer ambiente.
    /// <para>
    /// API e worker sobem em paralelo e os dois chamam este método. Não há corrida: o
    /// EF Core adquire lock exclusivo antes de migrar, então o segundo espera o
    /// primeiro terminar e encontra o schema em dia.
    /// </para>
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
