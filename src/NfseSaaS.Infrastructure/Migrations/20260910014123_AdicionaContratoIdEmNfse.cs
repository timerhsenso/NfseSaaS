using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaContratoIdEmNfse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContratoId",
                table: "notas_fiscais",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_ContratoId",
                table: "notas_fiscais",
                column: "ContratoId");

            migrationBuilder.AddForeignKey(
                name: "FK_notas_fiscais_contratos_ContratoId",
                table: "notas_fiscais",
                column: "ContratoId",
                principalTable: "contratos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notas_fiscais_contratos_ContratoId",
                table: "notas_fiscais");

            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_ContratoId",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ContratoId",
                table: "notas_fiscais");
        }
    }
}
