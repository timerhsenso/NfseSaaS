using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NumeroContrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Numero",
                table: "contratos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_contratos_TenantId_EmpresaId_Numero",
                table: "contratos",
                columns: new[] { "TenantId", "EmpresaId", "Numero" },
                unique: true,
                filter: "\"Numero\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_contratos_TenantId_EmpresaId_Numero",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "Numero",
                table: "contratos");
        }
    }
}
