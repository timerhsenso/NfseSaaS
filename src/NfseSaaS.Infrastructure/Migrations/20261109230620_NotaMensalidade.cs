using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotaMensalidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notas_mensalidade",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AsaasPagamentoId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    DataPagamento = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NfseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    UltimoErro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EmitidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstornoAvisadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notas_mensalidade", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notas_mensalidade_AsaasPagamentoId",
                table: "notas_mensalidade",
                column: "AsaasPagamentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_mensalidade_Status_CreatedAt",
                table: "notas_mensalidade",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notas_mensalidade_TenantId",
                table: "notas_mensalidade",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notas_mensalidade");
        }
    }
}
