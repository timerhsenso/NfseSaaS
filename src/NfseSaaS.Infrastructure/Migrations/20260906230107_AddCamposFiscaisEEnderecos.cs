using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCamposFiscaisEEnderecos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoNbs",
                table: "servicos",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CstPisCofins",
                table: "empresas",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "empresas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OpSimpNac",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PercentualTotalTributosSimplesNacional",
                table: "empresas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegApTribSN",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegEspTrib",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Telefone",
                table: "empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TpRetIssqn",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TpRetPisCofins",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TribIssqn",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Bairro",
                table: "clientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Cep",
                table: "clientes",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CodigoMunicipio",
                table: "clientes",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Logradouro",
                table: "clientes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Numero",
                table: "clientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoNbs",
                table: "servicos");

            migrationBuilder.DropColumn(
                name: "CstPisCofins",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "OpSimpNac",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "PercentualTotalTributosSimplesNacional",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "RegApTribSN",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "RegEspTrib",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Telefone",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "TpRetIssqn",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "TpRetPisCofins",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "TribIssqn",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "Bairro",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Cep",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "CodigoMunicipio",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Logradouro",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "Numero",
                table: "clientes");
        }
    }
}
