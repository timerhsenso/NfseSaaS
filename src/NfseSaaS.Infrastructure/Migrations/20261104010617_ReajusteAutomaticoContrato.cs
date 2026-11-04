using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReajusteAutomaticoContrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AplicarReajusteAutomatico",
                table: "empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AplicarIndiceNegativo",
                table: "contratos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReajusteAutomatico",
                table: "contratos",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AplicarReajusteAutomatico",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "AplicarIndiceNegativo",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "ReajusteAutomatico",
                table: "contratos");
        }
    }
}
