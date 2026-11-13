using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecursosPlano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "excecoes_recurso_conta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recurso = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Ligado = table.Column<bool>(type: "boolean", nullable: true),
                    Limite = table.Column<int>(type: "integer", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ValidaAte = table.Column<DateOnly>(type: "date", nullable: true),
                    CriadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_excecoes_recurso_conta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_excecoes_recurso_conta_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planos_recursos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recurso = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Ligado = table.Column<bool>(type: "boolean", nullable: true),
                    Limite = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planos_recursos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_planos_recursos_planos_PlanoId",
                        column: x => x.PlanoId,
                        principalTable: "planos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_excecoes_recurso_conta_TenantId_Recurso",
                table: "excecoes_recurso_conta",
                columns: new[] { "TenantId", "Recurso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planos_recursos_PlanoId_Recurso",
                table: "planos_recursos",
                columns: new[] { "PlanoId", "Recurso" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "excecoes_recurso_conta");

            migrationBuilder.DropTable(
                name: "planos_recursos");
        }
    }
}
