using Fiscal.Domain.Resumos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fiscal.Infrastructure.Persistencia.Configuracoes;

public sealed class ResumoEmitenteConfiguration : IEntityTypeConfiguration<ResumoEmitente>
{
    public void Configure(EntityTypeBuilder<ResumoEmitente> builder)
    {
        builder.ToTable("resumo_emitente");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.CnpjEmitente).HasMaxLength(14).IsRequired();
        builder.Property(r => r.Competencia).HasMaxLength(7).IsRequired();
        builder.Property(r => r.ValorTotal).HasPrecision(18, 2);

        builder.HasIndex(r => new { r.CnpjEmitente, r.Competencia })
            .IsUnique()
            .HasDatabaseName("ux_resumo_emitente_cnpj_competencia");
    }
}
