using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EncerramentoConta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DadosExpurgadosEm",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EncerradaEm",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EncerramentoOrigem",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpurgoPrevistoEm",
                table: "tenants",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DadosExpurgadosEm",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EncerradaEm",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EncerramentoOrigem",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "ExpurgoPrevistoEm",
                table: "tenants");
        }
    }
}
