using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class LotesEOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evento_pendente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MensagemId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublicadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Tentativas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evento_pendente", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lote_ingestao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CnpjProprietario = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    RecebidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lote_ingestao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lote_ingestao_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeArquivo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ChaveDeArmazenamento = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TamanhoEmBytes = table.Column<int>(type: "integer", nullable: false),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lote_ingestao_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lote_ingestao_item_lote_ingestao_LoteId",
                        column: x => x.LoteId,
                        principalTable: "lote_ingestao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evento_pendente_nao_publicado",
                table: "evento_pendente",
                column: "CriadoEm",
                filter: "\"PublicadoEm\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lote_ingestao_cnpj_recebido",
                table: "lote_ingestao",
                columns: new[] { "CnpjProprietario", "RecebidoEm" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_lote_ingestao_item_lote",
                table: "lote_ingestao_item",
                column: "LoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evento_pendente");

            migrationBuilder.DropTable(
                name: "lote_ingestao_item");

            migrationBuilder.DropTable(
                name: "lote_ingestao");
        }
    }
}
