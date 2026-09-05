using Fiscal.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

builder.Services.AddConsumidorDeIngestao(
    new OpcoesRabbitMq
    {
        Uri = builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:RabbitMq não configurada. O worker existe para consumir a fila; "
                + "sem broker ele não tem função."),
    });

var host = builder.Build();

// Os dois processos migram. Não há corrida: o EF adquire lock exclusivo, então quem
// chegar depois espera e encontra o schema em dia.
await host.Services.MigrarBancoAsync(CancellationToken.None);

await host.RunAsync();

/// <summary>Âncora para os testes de arquitetura alcançarem este assembly.</summary>
namespace Fiscal.Worker
{
    public sealed class PontoDeEntrada;
}
