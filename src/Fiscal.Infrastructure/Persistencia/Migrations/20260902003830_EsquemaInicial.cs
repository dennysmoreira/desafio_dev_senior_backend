using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class EsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documento_fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    ChaveAcesso = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    Numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Serie = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    CnpjEmitente = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    NomeEmitente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UfEmitente = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DocumentoDestinatario = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    NomeDestinatario = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DataEmissao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValorTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    HashConteudo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    XmlBruto = table.Column<byte[]>(type: "bytea", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecebidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Excluido = table.Column<bool>(type: "boolean", nullable: false),
                    ExcluidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documento_fiscal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mensagem_processada",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MensagemId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Consumidor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensagem_processada", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "resumo_emitente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CnpjEmitente = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    Competencia = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    QuantidadeDocumentos = table.Column<int>(type: "integer", nullable: false),
                    ValorTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resumo_emitente", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documento_fiscal_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Cfop = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Quantidade = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documento_fiscal_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documento_fiscal_item_documento_fiscal_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "documento_fiscal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_cnpj_data",
                table: "documento_fiscal",
                columns: new[] { "CnpjEmitente", "DataEmissao" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_uf",
                table: "documento_fiscal",
                column: "UfEmitente");

            migrationBuilder.CreateIndex(
                name: "ux_documento_fiscal_tipo_chave",
                table: "documento_fiscal",
                columns: new[] { "Tipo", "ChaveAcesso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_item_documento",
                table: "documento_fiscal_item",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "ux_mensagem_processada",
                table: "mensagem_processada",
                columns: new[] { "MensagemId", "Consumidor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_resumo_emitente_cnpj_competencia",
                table: "resumo_emitente",
                columns: new[] { "CnpjEmitente", "Competencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documento_fiscal_item");

            migrationBuilder.DropTable(
                name: "mensagem_processada");

            migrationBuilder.DropTable(
                name: "resumo_emitente");

            migrationBuilder.DropTable(
                name: "documento_fiscal");
        }
    }
}
