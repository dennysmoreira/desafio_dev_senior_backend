using Fiscal.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Fiscal.Infrastructure.Armazenamento;
using Fiscal.Infrastructure.Mensageria;

// Segundo deployável. Consome a fila e alimenta o resumo; não expõe HTTP.
//
// Existe separado da API por uma razão operacional, não estética: enquanto o
// consumidor era um serviço hospedado dentro do processo web, escalar a API para
// aguentar ingestão multiplicava consumidores sem que ninguém escolhesse isso, e um
// deploy rolling da API interrompia mensagens em processamento. Agora cada um escala
// e reinicia por conta própria.
//
// O código consumido é o mesmo: domínio, casos de uso e infraestrutura vêm das
// mesmas bibliotecas que a API usa. O que muda é só o host.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFiscalInfrastructure(
    builder.Configuration.GetConnectionString("Fiscal")
    ?? throw new InvalidOperationException("ConnectionStrings:Fiscal não configurada."));

// O worker LÊ do storage: baixa o XML que a API gravou. Sem este registro, a falha
// só apareceria na primeira mensagem — ver ValidateOnBuild abaixo.
builder.Services.AddArmazenamentoDeXml(new OpcoesDeArmazenamento
{
    Endpoint = builder.Configuration["Armazenamento:Endpoint"]
        ?? throw new InvalidOperationException("Armazenamento:Endpoint não configurado."),
    AccessKey = builder.Configuration["Armazenamento:AccessKey"]
        ?? throw new InvalidOperationException("Armazenamento:AccessKey não configurado."),
    SecretKey = builder.Configuration["Armazenamento:SecretKey"]
        ?? throw new InvalidOperationException("Armazenamento:SecretKey não configurado."),
});

builder.Services.AddConsumidorDeIngestao(
    new OpcoesRabbitMq
    {
        Uri = builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:RabbitMq não configurada. O worker existe para consumir a fila; "
                + "sem broker ele não tem função."),
    });

// Falha de composição derruba o processo no start, em vez de aparecer só quando a
// primeira mensagem chega. Foi exatamente o que aconteceu ao esquecer o registro do
// storage aqui: o consumidor classificou o erro como transitório, retentou três
// vezes e mandou quatro mensagens boas para a fila venenosa.
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true,
}));

var host = builder.Build();

// O worker NÃO migra: quem é dono do schema é a API. Duas migrações simultâneas
// contra um banco vazio derrubam uma delas — foi o que aconteceu na primeira vez que
// esta stack subiu inteira. Aqui só se espera o schema ficar pronto.
await host.Services.AguardarSchemaAsync(TimeSpan.FromMinutes(2), CancellationToken.None);

await host.RunAsync();

/// <summary>Âncora para os testes de arquitetura alcançarem este assembly.</summary>
namespace Fiscal.Worker
{
    public sealed class PontoDeEntrada;
}
