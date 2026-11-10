using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClienteCompartilhado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_EmpresaId_Apelido",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_EmpresaId_CpfCnpj",
                table: "clientes");

            migrationBuilder.AlterColumn<Guid>(
                name: "EmpresaId",
                table: "clientes",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_Apelido",
                table: "clientes",
                columns: new[] { "TenantId", "Apelido" },
                unique: true,
                filter: "\"EmpresaId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_CpfCnpj",
                table: "clientes",
                columns: new[] { "TenantId", "CpfCnpj" },
                unique: true,
                filter: "\"EmpresaId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_EmpresaId_Apelido",
                table: "clientes",
                columns: new[] { "TenantId", "EmpresaId", "Apelido" },
                unique: true,
                filter: "\"EmpresaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_EmpresaId_CpfCnpj",
                table: "clientes",
                columns: new[] { "TenantId", "EmpresaId", "CpfCnpj" },
                unique: true,
                filter: "\"EmpresaId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_Apelido",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_CpfCnpj",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_EmpresaId_Apelido",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "IX_clientes_TenantId_EmpresaId_CpfCnpj",
                table: "clientes");

            migrationBuilder.AlterColumn<Guid>(
                name: "EmpresaId",
                table: "clientes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_EmpresaId_Apelido",
                table: "clientes",
                columns: new[] { "TenantId", "EmpresaId", "Apelido" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TenantId_EmpresaId_CpfCnpj",
                table: "clientes",
                columns: new[] { "TenantId", "EmpresaId", "CpfCnpj" },
                unique: true);
        }
    }
}
