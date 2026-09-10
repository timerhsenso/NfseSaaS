using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaContratoEDiasAlertaEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasAlertaReajusteContratoPadrao",
                table: "empresas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "contratos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ValorAtual = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DataInicioContrato = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodicidadeReajusteMeses = table.Column<int>(type: "integer", nullable: false),
                    IndiceReajuste = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DataUltimoReajuste = table.Column<DateOnly>(type: "date", nullable: true),
                    DiasAlertaOverride = table.Column<int>(type: "integer", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contratos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contratos_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contratos_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contratos_servicos_ServicoId",
                        column: x => x.ServicoId,
                        principalTable: "servicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contratos_ClienteId",
                table: "contratos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_contratos_EmpresaId",
                table: "contratos",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_contratos_ServicoId",
                table: "contratos",
                column: "ServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_contratos_TenantId_ClienteId",
                table: "contratos",
                columns: new[] { "TenantId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_contratos_TenantId_EmpresaId",
                table: "contratos",
                columns: new[] { "TenantId", "EmpresaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contratos");

            migrationBuilder.DropColumn(
                name: "DiasAlertaReajusteContratoPadrao",
                table: "empresas");
        }
    }
}
