using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaDefaultTipoAmbienteNfse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TipoAmbiente",
                table: "notas_fiscais",
                type: "integer",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TipoAmbiente",
                table: "notas_fiscais",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 2);
        }
    }
}
