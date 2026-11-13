using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentosExigidosCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlertaDocumentos",
                table: "email_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentosAnexos",
                table: "email_logs",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentoPendenteEnvioAutomatico",
                table: "clientes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DocumentosExigidos",
                table: "clientes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlertaDocumentos",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "DocumentosAnexos",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "DocumentoPendenteEnvioAutomatico",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "DocumentosExigidos",
                table: "clientes");
        }
    }
}
