using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CadastroEmEtapas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Cnpj",
                table: "tenants",
                newName: "Documento");

            migrationBuilder.RenameIndex(
                name: "IX_tenants_Cnpj",
                table: "tenants",
                newName: "IX_tenants_Documento");

            migrationBuilder.AddColumn<int>(
                name: "Perfil",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Quem já usa o sistema não fica bloqueado de emitir em produção
            // (a confirmação de e-mail passa a ser exigida só para contas novas).
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"EmailConfirmed\" = TRUE WHERE \"EmailConfirmed\" = FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O UPDATE dos e-mails não é desfeito: não há como saber quem
            // estava sem confirmar antes, e confirmado é o estado seguro.
            migrationBuilder.DropColumn(
                name: "Perfil",
                table: "tenants");

            migrationBuilder.RenameColumn(
                name: "Documento",
                table: "tenants",
                newName: "Cnpj");

            migrationBuilder.RenameIndex(
                name: "IX_tenants_Documento",
                table: "tenants",
                newName: "IX_tenants_Cnpj");
        }
    }
}