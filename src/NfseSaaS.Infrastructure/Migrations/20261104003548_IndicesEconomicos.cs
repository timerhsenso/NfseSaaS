using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndicesEconomicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "indices_economicos_mensais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Indice = table.Column<int>(type: "integer", nullable: false),
                    Competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    Variacao = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Fonte = table.Column<int>(type: "integer", nullable: false),
                    AtualizadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indices_economicos_mensais", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_indices_economicos_mensais_Indice_Competencia",
                table: "indices_economicos_mensais",
                columns: new[] { "Indice", "Competencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indices_economicos_mensais");
        }
    }
}
