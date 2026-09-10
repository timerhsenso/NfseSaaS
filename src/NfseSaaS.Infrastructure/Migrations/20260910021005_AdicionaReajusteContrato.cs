using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaReajusteContrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reajustes_contrato",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataReajuste = table.Column<DateOnly>(type: "date", nullable: false),
                    ValorAnterior = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ValorNovo = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PercentualAplicado = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    IndiceUsado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reajustes_contrato", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reajustes_contrato_contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_contrato_ContratoId",
                table: "reajustes_contrato",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_contrato_TenantId_ContratoId",
                table: "reajustes_contrato",
                columns: new[] { "TenantId", "ContratoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reajustes_contrato");
        }
    }
}
