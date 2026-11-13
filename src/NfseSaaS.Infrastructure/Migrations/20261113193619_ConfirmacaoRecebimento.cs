using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmacaoRecebimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValidadeConfirmacaoDias",
                table: "modelos_email_nota",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "confirmacoes_recebimento_nota",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NfseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Principal = table.Column<bool>(type: "boolean", nullable: false),
                    Ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    ConfirmadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmacoes_recebimento_nota", x => x.Id);
                    table.ForeignKey(
                        name: "FK_confirmacoes_recebimento_nota_notas_fiscais_NfseId",
                        column: x => x.NfseId,
                        principalTable: "notas_fiscais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_confirmacoes_recebimento_nota_NfseId_Email",
                table: "confirmacoes_recebimento_nota",
                columns: new[] { "NfseId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_confirmacoes_recebimento_nota_TenantId_NfseId",
                table: "confirmacoes_recebimento_nota",
                columns: new[] { "TenantId", "NfseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "confirmacoes_recebimento_nota");

            migrationBuilder.DropColumn(
                name: "ValidadeConfirmacaoDias",
                table: "modelos_email_nota");
        }
    }
}
