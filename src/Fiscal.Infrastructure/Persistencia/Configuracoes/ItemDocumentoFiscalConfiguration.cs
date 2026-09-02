using Fiscal.Domain.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fiscal.Infrastructure.Persistencia.Configuracoes;

public sealed class ItemDocumentoFiscalConfiguration : IEntityTypeConfiguration<ItemDocumentoFiscal>
{
    public void Configure(EntityTypeBuilder<ItemDocumentoFiscal> builder)
    {
        builder.ToTable("documento_fiscal_item");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Codigo).HasMaxLength(60).IsRequired();
        builder.Property(i => i.Descricao).HasMaxLength(500).IsRequired();
        builder.Property(i => i.Ncm).HasMaxLength(8);
        builder.Property(i => i.Cfop).HasMaxLength(4);
        builder.Property(i => i.Quantidade).HasPrecision(18, 4);
        builder.Property(i => i.ValorUnitario).HasPrecision(18, 4);
        builder.Property(i => i.ValorTotal).HasPrecision(18, 2);

        builder.HasIndex(i => i.DocumentoId).HasDatabaseName("ix_documento_fiscal_item_documento");
    }
}
