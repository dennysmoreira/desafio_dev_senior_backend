using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.Application.Mensageria;
using Fiscal.Application.Resumos;
using Fiscal.Application.Seguranca;
using Microsoft.Extensions.DependencyInjection;

namespace Fiscal.IntegrationTests;

/// <summary>
/// Exercita o trabalho do consumidor contra o banco real, sem depender do broker.
/// A entrega em si é responsabilidade do RabbitMQ; o que precisa ser provado aqui é
/// que o efeito colateral resiste a reentrega — que é o que o item 8 pede quando
/// fala em "tratar reprocessamentos de forma segura".
/// </summary>
[TestFixture]
public sealed class ResumoDoConsumidorTests
{
    private const string Cnpj = "55555555000155";
    private const string CnpjVizinho = "66666666000166";

    private static readonly DateTimeOffset Emissao = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_mesma_mensagem_entregue_duas_vezes_conta_o_documento_uma_vez()
    {
        var evento = Evento("mensagem-repetida", Cnpj, 150.00m);

        await ConsumirAsync(evento);
        await ConsumirAsync(evento);

        var resumo = await ObterResumoAsync(Cnpj, "2026-03");

        using (Assert.EnterMultipleScope())
        {
            resumo.GetProperty("quantidadeDocumentos").GetInt32().ShouldBe(1);
            resumo.GetProperty("valorTotal").GetDecimal().ShouldBe(150.00m);
        }
    }

    [Test]
    public async Task Mensagens_distintas_acumulam_no_mesmo_periodo()
    {
        // Contraprova da anterior: sem ela, um consumidor que ignorasse tudo passaria.
        await ConsumirAsync(Evento("acumula-1", CnpjVizinho, 100.00m));
        await ConsumirAsync(Evento("acumula-2", CnpjVizinho, 250.50m));

        var resumo = await ObterResumoAsync(CnpjVizinho, "2026-03");

        using (Assert.EnterMultipleScope())
        {
            resumo.GetProperty("quantidadeDocumentos").GetInt32().ShouldBe(2);
            resumo.GetProperty("valorTotal").GetDecimal().ShouldBe(350.50m);
        }
    }

    [Test]
    public async Task Um_contribuinte_nao_enxerga_o_resumo_de_outro()
    {
        await ConsumirAsync(Evento("isolamento-resumo", Cnpj, 10.00m));

        using var cliente = AmbienteDeTeste.Cliente(CnpjVizinho);
        var resumos = await cliente.GetFromJsonAsync<JsonElement>("/resumos");

        resumos.EnumerateArray()
            .Select(r => r.GetProperty("cnpjEmitente").GetString())
            .ShouldNotContain(Cnpj);
    }

    private static DocumentoProcessado Evento(string mensagemId, string cnpj, decimal valor) =>
        new(mensagemId, Guid.CreateVersion7(), "chave-irrelevante-aqui", cnpj, Emissao, valor);

    /// <summary>Faz o que o consumidor faz ao receber a mensagem, incluindo o escopo de sistema.</summary>
    private static async Task ConsumirAsync(DocumentoProcessado evento)
    {
        using var escopo = AmbienteDeTeste.CriarEscopo();

        var definidor = escopo.ServiceProvider.GetRequiredService<IDefinidorContextoAcesso>();
        using var _ = definidor.AbrirEscopoDeSistema("teste do consumidor");

        var caso = escopo.ServiceProvider.GetRequiredService<AtualizarResumoDoEmitente>();

        await caso.ExecutarAsync(evento, CancellationToken.None);
    }

    private static async Task<JsonElement> ObterResumoAsync(string cnpj, string competencia)
    {
        using var cliente = AmbienteDeTeste.Cliente(cnpj);

        var resumos = await cliente.GetFromJsonAsync<JsonElement>("/resumos");

        return resumos.EnumerateArray()
            .First(r => r.GetProperty("competencia").GetString() == competencia);
    }
}
