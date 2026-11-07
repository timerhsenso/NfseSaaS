using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClienteEnvioEmailCopias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NaoEnviarNfsePorEmail",
                table: "clientes",
                newName: "EnviarNfsePorEmail");

            // O sentido inverteu (antes "não enviar"): todos começam SEM envio.
            migrationBuilder.Sql("UPDATE clientes SET \"EnviarNfsePorEmail\" = false;");

            migrationBuilder.AddColumn<string>(
                name: "Copias",
                table: "email_logs",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CopiasOcultas",
                table: "email_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CopiaOculta",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailsCopia",
                table: "clientes",
                type: "character varying(505)",
                maxLength: 505,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Copias",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "CopiasOcultas",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "CopiaOculta",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "EmailsCopia",
                table: "clientes");

            // Volta ao sentido antigo ("não enviar"): inverte antes de renomear.
            migrationBuilder.Sql("UPDATE clientes SET \"EnviarNfsePorEmail\" = NOT \"EnviarNfsePorEmail\";");

            migrationBuilder.RenameColumn(
                name: "EnviarNfsePorEmail",
                table: "clientes",
                newName: "NaoEnviarNfsePorEmail");
        }
    }
}