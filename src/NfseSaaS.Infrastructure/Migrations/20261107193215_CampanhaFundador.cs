using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CampanhaFundador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CampanhaAte",
                table: "vagas_fundador",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcessoAteAposCancelar",
                table: "assinaturas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FundadorReservaGrupo",
                table: "assinaturas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FundadorReservaSemPrazo",
                table: "assinaturas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FundadorReservadoEm",
                table: "assinaturas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PrimeiraCobrancaEm",
                table: "assinaturas",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CampanhaAte",
                table: "vagas_fundador");

            migrationBuilder.DropColumn(
                name: "AcessoAteAposCancelar",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "FundadorReservaGrupo",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "FundadorReservaSemPrazo",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "FundadorReservadoEm",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "PrimeiraCobrancaEm",
                table: "assinaturas");
        }
    }
}
