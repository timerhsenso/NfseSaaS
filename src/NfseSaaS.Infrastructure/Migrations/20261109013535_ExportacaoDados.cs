using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExportacaoDados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exportacoes_dados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConcluidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: true),
                    QuantidadeNotas = table.Column<int>(type: "integer", nullable: true),
                    BaixadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Erro = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exportacoes_dados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exportacoes_dados_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exportacoes_dados_Status_ExpiraEm",
                table: "exportacoes_dados",
                columns: new[] { "Status", "ExpiraEm" });

            migrationBuilder.CreateIndex(
                name: "IX_exportacoes_dados_TenantId_CreatedAt",
                table: "exportacoes_dados",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exportacoes_dados");
        }
    }
}
