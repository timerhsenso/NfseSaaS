using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClienteCpfCnpjUnicoPorEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_CpfCnpj",
                table: "clientes");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_EmpresaId_CpfCnpj",
                table: "clientes",
                columns: new[] { "TenantId", "EmpresaId", "CpfCnpj" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_EmpresaId_CpfCnpj",
                table: "clientes");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_CpfCnpj",
                table: "clientes",
                columns: new[] { "TenantId", "CpfCnpj" });
        }
    }
}
