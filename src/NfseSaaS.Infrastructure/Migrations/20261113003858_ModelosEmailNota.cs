using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelosEmailNota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_modelos_email_nota_empresas_EmpresaId",
                table: "modelos_email_nota");

            migrationBuilder.DropIndex(
                name: "IX_modelos_email_nota_EmpresaId",
                table: "modelos_email_nota");

            migrationBuilder.DropIndex(
                name: "IX_modelos_email_nota_TenantId",
                table: "modelos_email_nota");

            migrationBuilder.DropIndex(
                name: "IX_modelos_email_nota_TenantId_EmpresaId",
                table: "modelos_email_nota");

            migrationBuilder.AddColumn<string>(
                name: "Nome",
                table: "modelos_email_nota",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Padrao",
                table: "modelos_email_nota",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ModeloEmailNotaId",
                table: "empresas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModeloEmailNotaId",
                table: "clientes",
                type: "uuid",
                nullable: true);

            // Converte os modelos do patch anterior (um da conta e um por
            // empresa) em modelos reutilizáveis, sem perder texto: o da conta
            // vira "Padrão"; o de cada empresa vira um modelo com o nome dela,
            // já escolhido na própria empresa. Precisa vir ANTES de apagar
            // a coluna EmpresaId (o EF gera na ordem errada para isto).
            migrationBuilder.Sql("""
                UPDATE modelos_email_nota SET "Nome" = 'Padrão', "Padrao" = TRUE WHERE "EmpresaId" IS NULL;
                UPDATE modelos_email_nota m
                   SET "Nome" = LEFT(COALESCE(NULLIF(e."NomeFantasia", ''), e."RazaoSocial"), 40) || ' (' || e."Cnpj" || ')'
                  FROM empresas e WHERE m."EmpresaId" = e."Id";
                UPDATE empresas e SET "ModeloEmailNotaId" = m."Id"
                  FROM modelos_email_nota m WHERE m."EmpresaId" = e."Id";
                """);

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "modelos_email_nota");

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_TenantId",
                table: "modelos_email_nota",
                column: "TenantId",
                unique: true,
                filter: "\"Padrao\"");

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_TenantId_Nome",
                table: "modelos_email_nota",
                columns: new[] { "TenantId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_empresas_ModeloEmailNotaId",
                table: "empresas",
                column: "ModeloEmailNotaId");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_ModeloEmailNotaId",
                table: "clientes",
                column: "ModeloEmailNotaId");

            migrationBuilder.AddForeignKey(
                name: "FK_clientes_modelos_email_nota_ModeloEmailNotaId",
                table: "clientes",
                column: "ModeloEmailNotaId",
                principalTable: "modelos_email_nota",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_empresas_modelos_email_nota_ModeloEmailNotaId",
                table: "empresas",
                column: "ModeloEmailNotaId",
                principalTable: "modelos_email_nota",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clientes_modelos_email_nota_ModeloEmailNotaId",
                table: "clientes");

            migrationBuilder.DropForeignKey(
                name: "FK_empresas_modelos_email_nota_ModeloEmailNotaId",
                table: "empresas");

            migrationBuilder.DropIndex(
                name: "IX_modelos_email_nota_TenantId",
                table: "modelos_email_nota");

            migrationBuilder.DropIndex(
                name: "IX_modelos_email_nota_TenantId_Nome",
                table: "modelos_email_nota");

            migrationBuilder.DropIndex(
                name: "IX_empresas_ModeloEmailNotaId",
                table: "empresas");

            migrationBuilder.DropIndex(
                name: "IX_clientes_ModeloEmailNotaId",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Nome",
                table: "modelos_email_nota");

            migrationBuilder.DropColumn(
                name: "Padrao",
                table: "modelos_email_nota");

            migrationBuilder.DropColumn(
                name: "ModeloEmailNotaId",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "ModeloEmailNotaId",
                table: "clientes");

            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaId",
                table: "modelos_email_nota",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_EmpresaId",
                table: "modelos_email_nota",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_TenantId",
                table: "modelos_email_nota",
                column: "TenantId",
                unique: true,
                filter: "\"EmpresaId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_TenantId_EmpresaId",
                table: "modelos_email_nota",
                columns: new[] { "TenantId", "EmpresaId" },
                unique: true,
                filter: "\"EmpresaId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_modelos_email_nota_empresas_EmpresaId",
                table: "modelos_email_nota",
                column: "EmpresaId",
                principalTable: "empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
