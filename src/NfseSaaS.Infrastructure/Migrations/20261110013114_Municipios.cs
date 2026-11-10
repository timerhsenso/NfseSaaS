using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Municipios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "municipios",
                columns: table => new
                {
                    Codigo = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    NomeBusca = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_municipios", x => x.Codigo);
                });

            migrationBuilder.CreateIndex(
                name: "IX_municipios_NomeBusca",
                table: "municipios",
                column: "NomeBusca");

            migrationBuilder.CreateIndex(
                name: "IX_municipios_Uf_NomeBusca",
                table: "municipios",
                columns: new[] { "Uf", "NomeBusca" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "municipios");
        }
    }
}
