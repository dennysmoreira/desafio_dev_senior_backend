using System.Reflection;
using Fiscal.Application.Lotes;
using Fiscal.Domain.Documentos;
using Fiscal.Infrastructure.Persistencia;
using NetArchTest.Rules;

namespace Fiscal.ArchitectureTests;

/// <summary>
/// Cinco regras, não quinze.
/// <para>
/// Asserção de convenção de nome não prova arquitetura nenhuma — prova que alguém
/// digitou um sufixo. As regras aqui verificam a única coisa que um teste de
/// arquitetura consegue verificar e um humano não consegue vigiar: a direção das
/// dependências. Cada uma delas, se quebrada, quebra uma decisão de desenho
/// registrada no README.
/// </para>
/// <para>
/// Cada regra foi confirmada injetando uma violação real e vendo o teste falhar —
/// teste de arquitetura que nunca falhou não é garantia, é decoração.
/// </para>
/// <para>
/// Limitação conhecida: o NetArchTest lê o IL, não o código-fonte. Um uso que o
/// compilador resolve antes de gerar IL, como <c>nameof(DbContext)</c>, não deixa
/// referência e passa despercebido. Na prática não incomoda — o que quebra
/// arquitetura é usar o tipo, e usar deixa rastro.
/// </para>
/// </summary>
[TestFixture]
public sealed class RegrasDeArquiteturaTests
{
    private static readonly Assembly Dominio = typeof(DocumentoFiscal).Assembly;
    private static readonly Assembly Aplicacao = typeof(IngerirArquivo).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly Assembly Worker = typeof(Fiscal.Worker.PontoDeEntrada).Assembly;

    /// <summary>
    /// O domínio é o centro: as regras do documento fiscal não podem depender de
    /// como ele é guardado, entregue ou exposto. Se esta regra cair, mover de
    /// PostgreSQL para outro banco deixa de ser uma decisão local.
    /// </summary>
    [Test]
    public void O_dominio_nao_depende_de_nenhuma_outra_camada() =>
        Verificar(
            Types.InAssembly(Dominio)
                .ShouldNot()
                .HaveDependencyOnAny("Fiscal.Application", "Fiscal.Infrastructure", "Fiscal.Api"));

    /// <summary>
    /// Casos de uso conhecem interfaces, não implementações. É o que permite os
    /// testes unitários de ingestão rodarem com repositório e publicador falsos, sem
    /// banco nem fila.
    /// </summary>
    [Test]
    public void A_aplicacao_nao_depende_de_infraestrutura_nem_da_api() =>
        Verificar(
            Types.InAssembly(Aplicacao)
                .ShouldNot()
                .HaveDependencyOnAny("Fiscal.Infrastructure", "Fiscal.Api"));

    /// <summary>
    /// A regra que mais diz sobre o desenho. Se um endpoint ou um caso de uso
    /// conhecesse <c>RabbitMQ.Client</c>, trocar de broker viraria uma varredura pelo
    /// código inteiro — e testar o consumo exigiria um broker de verdade.
    /// <para>
    /// Vale inclusive para o worker, cuja razão de existir é consumir a fila: ele
    /// compõe o consumidor por uma extensão da infraestrutura e não toca no cliente
    /// do broker.
    /// </para>
    /// </summary>
    [Test]
    public void Nada_fora_da_infraestrutura_conhece_o_cliente_do_rabbitmq()
    {
        foreach (var assembly in new[] { Dominio, Aplicacao, Api, Worker })
        {
            Verificar(Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("RabbitMQ"));
        }
    }

    /// <summary>
    /// Mesma ideia para persistência. É por isto que a composição do EF Core mora
    /// numa extensão dentro da própria infraestrutura: sem ela, a API precisaria
    /// chamar <c>AddDbContext</c> e passaria a depender do EF.
    /// </summary>
    [Test]
    public void Nada_fora_da_infraestrutura_conhece_o_ef_core_nem_o_npgsql()
    {
        foreach (var assembly in new[] { Dominio, Aplicacao, Api, Worker })
        {
            Verificar(
                Types.InAssembly(assembly)
                    .ShouldNot()
                    .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql"));
        }
    }

    /// <summary>
    /// O domínio não tem um único PackageReference. Esta regra guarda isso contra o
    /// caminho mais comum de erosão: alguém precisa de um logger dentro de uma
    /// entidade e adiciona "só uma abstração".
    /// </summary>
    [Test]
    public void O_dominio_nao_depende_de_framework_algum() =>
        Verificar(
            Types.InAssembly(Dominio)
                .ShouldNot()
                .HaveDependencyOnAny("Microsoft.Extensions", "Microsoft.AspNetCore", "Polly"));

    private static void Verificar(ConditionList condicao)
    {
        var resultado = condicao.GetResult();

        var infratores = resultado.FailingTypeNames is null
            ? string.Empty
            : string.Join(", ", resultado.FailingTypeNames);

        resultado.IsSuccessful.ShouldBeTrue($"Tipos violando a regra: {infratores}");
    }
}
