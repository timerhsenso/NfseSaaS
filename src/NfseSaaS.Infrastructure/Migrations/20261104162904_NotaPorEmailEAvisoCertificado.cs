using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotaPorEmailEAvisoCertificado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChaveDeduplicacao",
                table: "email_logs",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NfseId",
                table: "email_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NaoEnviarNfsePorEmail",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_email_logs_TenantId_ChaveDeduplicacao",
                table: "email_logs",
                columns: new[] { "TenantId", "ChaveDeduplicacao" },
                filter: "\"ChaveDeduplicacao\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_email_logs_TenantId_ChaveDeduplicacao",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "ChaveDeduplicacao",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "NfseId",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "NaoEnviarNfsePorEmail",
                table: "clientes");
        }
    }
}
