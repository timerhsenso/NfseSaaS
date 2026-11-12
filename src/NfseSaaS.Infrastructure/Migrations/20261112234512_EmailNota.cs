using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmailNota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CopiaOcultaFixa",
                table: "email_logs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "modelos_email_nota",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Assunto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ResponderPara = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CopiaOcultaPara = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OcultarDivulgacao = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modelos_email_nota", x => x.Id);
                    table.ForeignKey(
                        name: "FK_modelos_email_nota_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_modelos_email_nota_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_EmpresaId",
                table: "modelos_email_nota",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_TenantId",
                table: "modelos_email_nota",
                column: "TenantId",
                unique: true,
                filter: "\"EmpresaId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_modelos_email_nota_TenantId_EmpresaId",
                table: "modelos_email_nota",
                columns: new[] { "TenantId", "EmpresaId" },
                unique: true,
                filter: "\"EmpresaId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "modelos_email_nota");

            migrationBuilder.DropColumn(
                name: "CopiaOcultaFixa",
                table: "email_logs");
        }
    }
}
