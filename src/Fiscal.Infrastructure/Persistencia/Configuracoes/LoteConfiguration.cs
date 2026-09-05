using Fiscal.Domain.Lotes;
using Fiscal.Domain.Processamento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fiscal.Infrastructure.Persistencia.Configuracoes;

public sealed class LoteConfiguration : IEntityTypeConfiguration<LoteDeIngestao>
{
    public void Configure(EntityTypeBuilder<LoteDeIngestao> builder)
    {
        builder.ToTable("lote_ingestao");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.CnpjProprietario).HasMaxLength(14).IsRequired();

        // A listagem de lotes é sempre "os meus, do mais recente para o mais antigo".
        builder.HasIndex(l => new { l.CnpjProprietario, l.RecebidoEm })
            .IsDescending(false, true)
            .HasDatabaseName("ix_lote_ingestao_cnpj_recebido");

        builder.HasMany(l => l.Itens)
            .WithOne()
            .HasForeignKey(i => i.LoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(l => l.Itens)
            .HasField("_itens")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ItemDoLoteConfiguration : IEntityTypeConfiguration<ItemDoLote>
{
    public void Configure(EntityTypeBuilder<ItemDoLote> builder)
    {
        builder.ToTable("lote_ingestao_item");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.NomeArquivo).HasMaxLength(260).IsRequired();
        builder.Property(i => i.ChaveDeArmazenamento).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Motivo).HasMaxLength(500);

        builder.HasIndex(i => i.LoteId).HasDatabaseName("ix_lote_ingestao_item_lote");
    }
}

public sealed class EventoPendenteConfiguration : IEntityTypeConfiguration<EventoPendente>
{
    public void Configure(EntityTypeBuilder<EventoPendente> builder)
    {
        builder.ToTable("evento_pendente");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.MensagemId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Payload).IsRequired();

        // O relay busca só o que ainda não foi publicado, em ordem de criação. O
        // índice é parcial: linhas já publicadas não interessam à varredura e não
        // precisam ocupar o índice.
        builder.HasIndex(e => e.CriadoEm)
            .HasFilter("\"PublicadoEm\" IS NULL")
            .HasDatabaseName("ix_evento_pendente_nao_publicado");
    }
}
