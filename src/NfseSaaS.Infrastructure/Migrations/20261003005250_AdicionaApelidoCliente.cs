using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaApelidoCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Apelido",
                table: "clientes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Preenche o Apelido dos clientes existentes ANTES do índice
            // único: razão social cortada em 40 caracteres; nomes repetidos
            // na mesma Empresa (sem diferenciar maiúsculas) ganham os 6
            // últimos dígitos do CPF/CNPJ — mesmo critério de
            // ApelidoCliente.GerarAPartirDoNomeAsync.
            migrationBuilder.Sql("""
                UPDATE clientes SET "Apelido" = LEFT(BTRIM("Nome"), 40);

                WITH duplicados AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "TenantId", "EmpresaId", LOWER("Apelido")
                               ORDER BY "CreatedAt", "Id") AS ordem
                    FROM clientes)
                UPDATE clientes c
                SET "Apelido" = RTRIM(LEFT(BTRIM(c."Nome"), 33)) || ' ' || RIGHT(c."CpfCnpj", 6)
                FROM duplicados d
                WHERE d."Id" = c."Id" AND d.ordem > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_EmpresaId_Apelido",
                table: "clientes",
                columns: new[] { "TenantId", "EmpresaId", "Apelido" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_EmpresaId_Apelido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Apelido",
                table: "clientes");
        }
    }
}