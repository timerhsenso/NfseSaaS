using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaEnderecoEmpresaCamposFiscaisEIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoNbs",
                table: "notas_fiscais",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CodigoTributacaoNacional",
                table: "notas_fiscais",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CstPisCofins",
                table: "notas_fiscais",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "notas_fiscais",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PercentualTotalTributosSimplesNacional",
                table: "notas_fiscais",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TpRetIssqn",
                table: "notas_fiscais",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TpRetPisCofins",
                table: "notas_fiscais",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TribIssqn",
                table: "notas_fiscais",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Bairro",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Cep",
                table: "empresas",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Complemento",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Logradouro",
                table: "empresas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Numero",
                table: "empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Uf",
                table: "empresas",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "contadores_dps",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerieDps = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    UltimoNumero = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contadores_dps", x => new { x.EmpresaId, x.SerieDps });
                });

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_TenantId_EmpresaId_IdempotencyKey",
                table: "notas_fiscais",
                columns: new[] { "TenantId", "EmpresaId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contadores_dps");

            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_TenantId_EmpresaId_IdempotencyKey",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "CodigoNbs",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "CodigoTributacaoNacional",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "CstPisCofins",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "PercentualTotalTributosSimplesNacional",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "TpRetIssqn",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "TpRetPisCofins",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "TribIssqn",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "Bairro",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Cep",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Complemento",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Logradouro",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Numero",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Uf",
                table: "empresas");
        }
    }
}
