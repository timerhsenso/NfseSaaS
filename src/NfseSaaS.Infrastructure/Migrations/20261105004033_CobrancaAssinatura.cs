using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CobrancaAssinatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assinaturas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Plano = table.Column<int>(type: "integer", nullable: true),
                    PrecoMensal = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Fundador = table.Column<bool>(type: "boolean", nullable: false),
                    TesteIniciadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AsaasClienteId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AsaasAssinaturaId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AssinadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtrasadaDesde = table.Column<DateOnly>(type: "date", nullable: true),
                    UltimoPagamentoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanceladaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UrlFaturaPendente = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assinaturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assinaturas_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_cobranca",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AsaasPagamentoId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AsaasAssinaturaId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Processado = table.Column<bool>(type: "boolean", nullable: false),
                    Resultado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos_cobranca", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assinaturas_AsaasAssinaturaId",
                table: "assinaturas",
                column: "AsaasAssinaturaId",
                unique: true,
                filter: "\"AsaasAssinaturaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_assinaturas_TenantId",
                table: "assinaturas",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_eventos_cobranca_EventoId",
                table: "eventos_cobranca",
                column: "EventoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_eventos_cobranca_TenantId_CreatedAt",
                table: "eventos_cobranca",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assinaturas");

            migrationBuilder.DropTable(
                name: "eventos_cobranca");
        }
    }
}
