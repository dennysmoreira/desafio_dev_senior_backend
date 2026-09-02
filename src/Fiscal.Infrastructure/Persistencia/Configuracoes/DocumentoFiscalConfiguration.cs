using Fiscal.Domain.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fiscal.Infrastructure.Persistencia.Configuracoes;

public sealed class DocumentoFiscalConfiguration : IEntityTypeConfiguration<DocumentoFiscal>
{
    public void Configure(EntityTypeBuilder<DocumentoFiscal> builder)
    {
        builder.ToTable("documento_fiscal");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.ChaveAcesso).HasMaxLength(44).IsRequired();
        builder.Property(d => d.Numero).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Serie).HasMaxLength(5).IsRequired();
        builder.Property(d => d.CnpjEmitente).HasMaxLength(14).IsRequired();
        builder.Property(d => d.NomeEmitente).HasMaxLength(200).IsRequired();
        builder.Property(d => d.UfEmitente).HasMaxLength(2).IsRequired();
        builder.Property(d => d.DocumentoDestinatario).HasMaxLength(14);
        builder.Property(d => d.NomeDestinatario).HasMaxLength(200);
        builder.Property(d => d.ValorTotal).HasPrecision(18, 2);
        builder.Property(d => d.HashConteudo).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Observacao).HasMaxLength(1000);

        // A âncora da idempotência. O índice é único e SEM filtro de exclusão
        // lógica de propósito: um documento excluído logicamente continua ocupando
        // sua chave, senão reenviar o XML criaria uma segunda linha para o mesmo
        // documento fiscal.
        builder.HasIndex(d => new { d.Tipo, d.ChaveAcesso })
            .IsUnique()
            .HasDatabaseName("ux_documento_fiscal_tipo_chave");

        // Cobre exatamente os filtros que o enunciado pede (CNPJ, data) já na
        // ordem de leitura da listagem, que é decrescente por data de emissão.
        builder.HasIndex(d => new { d.CnpjEmitente, d.DataEmissao })
            .IsDescending(false, true)
            .HasDatabaseName("ix_documento_fiscal_cnpj_data");

        builder.HasIndex(d => d.UfEmitente).HasDatabaseName("ix_documento_fiscal_uf");

        // Filtrar por CNPJ do emitente seria inócuo — o filtro global já prende a
        // consulta ao CNPJ autenticado. O filtro por CNPJ que produz resultado útil
        // é o do destinatário: "quais notas emiti para o cliente X".
        builder.HasIndex(d => new { d.DocumentoDestinatario, d.DataEmissao })
            .IsDescending(false, true)
            .HasDatabaseName("ix_documento_fiscal_destinatario_data");

        builder.HasMany(d => d.Itens)
            .WithOne()
            .HasForeignKey(i => i.DocumentoId)
            .OnDelete(DeleteBehavior.Cascade);

        // A coleção é exposta como somente-leitura; o EF escreve no campo de apoio.
        builder.Navigation(d => d.Itens)
            .HasField("_itens")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
