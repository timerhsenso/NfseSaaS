using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IbsCbsServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CClassTrib",
                table: "servicos",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CIndOp",
                table: "servicos",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CClassTrib",
                table: "notas_fiscais",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CIndOp",
                table: "notas_fiscais",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CClassTrib",
                table: "servicos");

            migrationBuilder.DropColumn(
                name: "CIndOp",
                table: "servicos");

            migrationBuilder.DropColumn(
                name: "CClassTrib",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "CIndOp",
                table: "notas_fiscais");
        }
    }
}
