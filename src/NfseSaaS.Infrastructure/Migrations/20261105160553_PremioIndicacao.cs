using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PremioIndicacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "premios_indicacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IndicadoTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Papel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AsaasPagamentoOrigemId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    AplicadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AsaasPagamentoDispensadoId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    VencimentoDispensado = table.Column<DateOnly>(type: "date", nullable: true),
                    Detalhe = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_premios_indicacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_premios_indicacao_tenants_IndicadoTenantId",
                        column: x => x.IndicadoTenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_premios_indicacao_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_premios_indicacao_IndicadoTenantId_Papel",
                table: "premios_indicacao",
                columns: new[] { "IndicadoTenantId", "Papel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_premios_indicacao_Status",
                table: "premios_indicacao",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_premios_indicacao_TenantId_Status",
                table: "premios_indicacao",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "premios_indicacao");
        }
    }
}
