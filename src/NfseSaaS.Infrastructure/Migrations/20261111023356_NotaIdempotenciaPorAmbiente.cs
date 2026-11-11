using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotaIdempotenciaPorAmbiente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_TenantId_EmpresaId_IdempotencyKey",
                table: "notas_fiscais");

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_TenantId_EmpresaId_TipoAmbiente_IdempotencyKey",
                table: "notas_fiscais",
                columns: new[] { "TenantId", "EmpresaId", "TipoAmbiente", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_TenantId_EmpresaId_TipoAmbiente_IdempotencyKey",
                table: "notas_fiscais");

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_TenantId_EmpresaId_IdempotencyKey",
                table: "notas_fiscais",
                columns: new[] { "TenantId", "EmpresaId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }
    }
}
