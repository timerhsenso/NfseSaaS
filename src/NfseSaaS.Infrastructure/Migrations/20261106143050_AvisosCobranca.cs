using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AvisosCobranca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ValorFaturaPendente",
                table: "assinaturas",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "VencimentoFaturaPendente",
                table: "assinaturas",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValorFaturaPendente",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "VencimentoFaturaPendente",
                table: "assinaturas");
        }
    }
}
