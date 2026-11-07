using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TesteGratisPorCnpj : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SemTesteGratis",
                table: "assinaturas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "testes_gratis_cnpj",
                columns: table => new
                {
                    Cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IniciadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_testes_gratis_cnpj", x => x.Cnpj);
                });

            migrationBuilder.CreateIndex(
                name: "IX_testes_gratis_cnpj_TenantId",
                table: "testes_gratis_cnpj",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "testes_gratis_cnpj");

            migrationBuilder.DropColumn(
                name: "SemTesteGratis",
                table: "assinaturas");
        }
    }
}
