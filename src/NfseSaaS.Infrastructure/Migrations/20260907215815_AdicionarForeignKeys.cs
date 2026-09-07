using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_servicos_EmpresaId",
                table: "servicos",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_ClienteId",
                table: "notas_fiscais",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_EmpresaId",
                table: "notas_fiscais",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_EmpresaId",
                table: "clientes",
                column: "EmpresaId");

            migrationBuilder.AddForeignKey(
                name: "FK_clientes_empresas_EmpresaId",
                table: "clientes",
                column: "EmpresaId",
                principalTable: "empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notas_fiscais_clientes_ClienteId",
                table: "notas_fiscais",
                column: "ClienteId",
                principalTable: "clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notas_fiscais_empresas_EmpresaId",
                table: "notas_fiscais",
                column: "EmpresaId",
                principalTable: "empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_servicos_empresas_EmpresaId",
                table: "servicos",
                column: "EmpresaId",
                principalTable: "empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clientes_empresas_EmpresaId",
                table: "clientes");

            migrationBuilder.DropForeignKey(
                name: "FK_notas_fiscais_clientes_ClienteId",
                table: "notas_fiscais");

            migrationBuilder.DropForeignKey(
                name: "FK_notas_fiscais_empresas_EmpresaId",
                table: "notas_fiscais");

            migrationBuilder.DropForeignKey(
                name: "FK_servicos_empresas_EmpresaId",
                table: "servicos");

            migrationBuilder.DropIndex(
                name: "IX_servicos_EmpresaId",
                table: "servicos");

            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_ClienteId",
                table: "notas_fiscais");

            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_EmpresaId",
                table: "notas_fiscais");

            migrationBuilder.DropIndex(
                name: "IX_clientes_EmpresaId",
                table: "clientes");
        }
    }
}
