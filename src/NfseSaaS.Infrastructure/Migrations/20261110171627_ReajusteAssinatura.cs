using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReajusteAssinatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reajustes_assinatura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssinaturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plano = table.Column<int>(type: "integer", nullable: false),
                    PrecoAnterior = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PrecoNovo = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    AplicarEm = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AvisadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AplicadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Detalhe = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    CriadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reajustes_assinatura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reajustes_assinatura_assinaturas_AssinaturaId",
                        column: x => x.AssinaturaId,
                        principalTable: "assinaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reajustes_assinatura_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_assinatura_AssinaturaId",
                table: "reajustes_assinatura",
                column: "AssinaturaId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_assinatura_LoteId",
                table: "reajustes_assinatura",
                column: "LoteId");

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_assinatura_Status_AplicarEm",
                table: "reajustes_assinatura",
                columns: new[] { "Status", "AplicarEm" });

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_assinatura_TenantId_Status",
                table: "reajustes_assinatura",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reajustes_assinatura");
        }
    }
}
