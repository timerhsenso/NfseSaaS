using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentosEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documentos_empresa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    NomePersonalizado = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    NomeChave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DataEmissao = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidaAte = table.Column<DateOnly>(type: "date", nullable: true),
                    NomeArquivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    EnviadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentos_empresa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documentos_empresa_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documentos_empresa_EmpresaId",
                table: "documentos_empresa",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_documentos_empresa_TenantId_EmpresaId_Tipo_NomeChave_Create~",
                table: "documentos_empresa",
                columns: new[] { "TenantId", "EmpresaId", "Tipo", "NomeChave", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentos_empresa");
        }
    }
}
