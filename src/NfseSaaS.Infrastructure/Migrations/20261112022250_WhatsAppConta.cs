using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppConta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LembreteWhatsAppAdiadoAte",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp",
                table: "tenants",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WhatsAppAutorizadoEm",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LembreteWhatsAppAdiadoAte",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "WhatsApp",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "WhatsAppAutorizadoEm",
                table: "tenants");
        }
    }
}
