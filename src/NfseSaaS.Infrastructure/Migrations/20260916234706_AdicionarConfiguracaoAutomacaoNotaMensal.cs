using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarConfiguracaoAutomacaoNotaMensal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuracoes_automacao_nota_mensal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    Frequencia = table.Column<int>(type: "integer", nullable: false),
                    DiaSemana = table.Column<int>(type: "integer", nullable: true),
                    DiaDoMes = table.Column<int>(type: "integer", nullable: true),
                    Horario = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Modo = table.Column<int>(type: "integer", nullable: false),
                    CompetenciasSemConfirmacao = table.Column<int>(type: "integer", nullable: false),
                    DesligadoPorInatividade = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracoes_automacao_nota_mensal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuracoes_automacao_nota_mensal_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configuracoes_automacao_nota_mensal_EmpresaId",
                table: "configuracoes_automacao_nota_mensal",
                column: "EmpresaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracoes_automacao_nota_mensal");
        }
    }
}
