using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubstituicaoNfse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescricaoMotivoSubstituicao",
                table: "notas_fiscais",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoSubstituicao",
                table: "notas_fiscais",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubstituiNfseId",
                table: "notas_fiscais",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_fiscais_SubstituiNfseId",
                table: "notas_fiscais",
                column: "SubstituiNfseId",
                unique: true,
                filter: "\"SubstituiNfseId\" IS NOT NULL AND \"Status\" IN ('Processando', 'Autorizada')");

            migrationBuilder.AddForeignKey(
                name: "FK_notas_fiscais_notas_fiscais_SubstituiNfseId",
                table: "notas_fiscais",
                column: "SubstituiNfseId",
                principalTable: "notas_fiscais",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notas_fiscais_notas_fiscais_SubstituiNfseId",
                table: "notas_fiscais");

            migrationBuilder.DropIndex(
                name: "IX_notas_fiscais_SubstituiNfseId",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "DescricaoMotivoSubstituicao",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "MotivoSubstituicao",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "SubstituiNfseId",
                table: "notas_fiscais");
        }
    }
}
