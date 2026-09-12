using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaEmailLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Destinatario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Assunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorpoHtml = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErroDetalhe = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    EnviadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_logs_TenantId_Status_CreatedAt",
                table: "email_logs",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_logs");
        }
    }
}
