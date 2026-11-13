using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetencoesFederais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TpRetPisCofins",
                table: "empresas");

            migrationBuilder.AddColumn<decimal>(
                name: "DescontoCondicionado",
                table: "notas_fiscais",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DescontoIncondicionado",
                table: "notas_fiscais",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorRetidoCofins",
                table: "notas_fiscais",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorRetidoCsll",
                table: "notas_fiscais",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorRetidoInss",
                table: "notas_fiscais",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorRetidoIrrf",
                table: "notas_fiscais",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorRetidoPis",
                table: "notas_fiscais",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AvisoDispensaRetencaoNaDescricao",
                table: "empresas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RetencaoCofinsPercentual",
                table: "clientes",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetencaoCsllPercentual",
                table: "clientes",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetencaoInssPercentual",
                table: "clientes",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetencaoIrrfPercentual",
                table: "clientes",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetencaoPisPercentual",
                table: "clientes",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescontoCondicionado",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "DescontoIncondicionado",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ValorRetidoCofins",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ValorRetidoCsll",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ValorRetidoInss",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ValorRetidoIrrf",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ValorRetidoPis",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "AvisoDispensaRetencaoNaDescricao",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "RetencaoCofinsPercentual",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "RetencaoCsllPercentual",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "RetencaoInssPercentual",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "RetencaoIrrfPercentual",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "RetencaoPisPercentual",
                table: "clientes");

            migrationBuilder.AddColumn<string>(
                name: "TpRetPisCofins",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");
        }
    }
}
