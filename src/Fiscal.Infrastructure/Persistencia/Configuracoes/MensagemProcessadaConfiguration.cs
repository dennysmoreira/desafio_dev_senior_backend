using Fiscal.Domain.Processamento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fiscal.Infrastructure.Persistencia.Configuracoes;

public sealed class MensagemProcessadaConfiguration : IEntityTypeConfiguration<MensagemProcessada>
{
    public void Configure(EntityTypeBuilder<MensagemProcessada> builder)
    {
        builder.ToTable("mensagem_processada");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.MensagemId).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Consumidor).HasMaxLength(100).IsRequired();

        // Chave única por (mensagem, consumidor): dois consumidores diferentes podem
        // e devem processar a mesma mensagem; o mesmo consumidor, não.
        builder.HasIndex(m => new { m.MensagemId, m.Consumidor })
            .IsUnique()
            .HasDatabaseName("ux_mensagem_processada");
    }
}
