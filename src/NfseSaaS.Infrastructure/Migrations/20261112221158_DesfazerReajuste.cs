using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DesfazerReajuste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DataUltimoReajusteAnterior",
                table: "movimentacoes_valor_contrato",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DesfeitoEm",
                table: "movimentacoes_valor_contrato",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DesfeitoPorUsuarioId",
                table: "movimentacoes_valor_contrato",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoDesfazer",
                table: "movimentacoes_valor_contrato",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataUltimoReajusteAnterior",
                table: "movimentacoes_valor_contrato");

            migrationBuilder.DropColumn(
                name: "DesfeitoEm",
                table: "movimentacoes_valor_contrato");

            migrationBuilder.DropColumn(
                name: "DesfeitoPorUsuarioId",
                table: "movimentacoes_valor_contrato");

            migrationBuilder.DropColumn(
                name: "MotivoDesfazer",
                table: "movimentacoes_valor_contrato");
        }
    }
}
