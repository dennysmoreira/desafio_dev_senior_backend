using Fiscal.Application.Seguranca;
using Fiscal.Domain.Documentos;
using Fiscal.Domain.Processamento;
using Fiscal.Domain.Resumos;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Infrastructure.Persistencia;

public sealed class FiscalDbContext(DbContextOptions<FiscalDbContext> options, IContextoAcesso contexto)
    : DbContext(options)
{
    public DbSet<DocumentoFiscal> Documentos => Set<DocumentoFiscal>();

    public DbSet<ResumoEmitente> Resumos => Set<ResumoEmitente>();

    public DbSet<MensagemProcessada> MensagensProcessadas => Set<MensagemProcessada>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FiscalDbContext).Assembly);

        // Isolamento e exclusão lógica aplicados no modelo, não nas consultas.
        // Qualquer LINQ escrito daqui pra frente herda os dois filtros; sair deles
        // exige IgnoreQueryFilters() explícito, que é visível em code review.
        //
        // CnpjAutorizado só é nulo dentro de um escopo de sistema explicitamente
        // aberto e logado; fora disso a leitura lança e a consulta falha, em vez de
        // devolver dados de todos os CNPJs.
        modelBuilder.Entity<DocumentoFiscal>().HasQueryFilter(documento =>
            !documento.Excluido
            && (contexto.CnpjAutorizado == null || documento.CnpjEmitente == contexto.CnpjAutorizado));

        modelBuilder.Entity<ResumoEmitente>().HasQueryFilter(resumo =>
            contexto.CnpjAutorizado == null || resumo.CnpjEmitente == contexto.CnpjAutorizado);

        base.OnModelCreating(modelBuilder);
    }
}
