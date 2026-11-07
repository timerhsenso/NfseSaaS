using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperadorCobranca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "IsentaAte",
                table: "assinaturas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsentaPelaPlataforma",
                table: "assinaturas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MotivoIsencao",
                table: "assinaturas",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TesteDiasExtras",
                table: "assinaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsentaAte",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "IsentaPelaPlataforma",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "MotivoIsencao",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "TesteDiasExtras",
                table: "assinaturas");
        }
    }
}
