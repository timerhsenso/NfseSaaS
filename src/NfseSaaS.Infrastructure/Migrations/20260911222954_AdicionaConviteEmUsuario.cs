using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaConviteEmUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConvidadoEm",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConviteAceitoEm",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConvidadoEm",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ConviteAceitoEm",
                table: "AspNetUsers");
        }
    }
}
