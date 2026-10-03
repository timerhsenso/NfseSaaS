using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaReenvioDeNfse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReenvioDeNfseId",
                table: "notas_fiscais",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServicoId",
                table: "notas_fiscais",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_ReenvioDeNfseId",
                table: "notas_fiscais",
                column: "ReenvioDeNfseId",
                unique: true,
                filter: "\"ReenvioDeNfseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_ServicoId",
                table: "notas_fiscais",
                column: "ServicoId");

            migrationBuilder.AddForeignKey(
                name: "FK_notas_fiscais_notas_fiscais_ReenvioDeNfseId",
                table: "notas_fiscais",
                column: "ReenvioDeNfseId",
                principalTable: "notas_fiscais",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notas_fiscais_servicos_ServicoId",
                table: "notas_fiscais",
                column: "ServicoId",
                principalTable: "servicos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notas_fiscais_notas_fiscais_ReenvioDeNfseId",
                table: "notas_fiscais");

            migrationBuilder.DropForeignKey(
                name: "FK_notas_fiscais_servicos_ServicoId",
                table: "notas_fiscais");

            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_ReenvioDeNfseId",
                table: "notas_fiscais");

            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_ServicoId",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ReenvioDeNfseId",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "ServicoId",
                table: "notas_fiscais");
        }
    }
}
