using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoPlanos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Publico = table.Column<int>(type: "integer", nullable: false),
                    OfertarParaEmpresa = table.Column<bool>(type: "boolean", nullable: false),
                    OfertarParaContador = table.Column<bool>(type: "boolean", nullable: false),
                    MaxEmpresas = table.Column<int>(type: "integer", nullable: false),
                    NotasPorMes = table.Column<int>(type: "integer", nullable: true),
                    AVenda = table.Column<bool>(type: "boolean", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vagas_fundador",
                columns: table => new
                {
                    Publico = table.Column<int>(type: "integer", nullable: false),
                    Quantidade = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vagas_fundador", x => x.Publico);
                });

            migrationBuilder.CreateTable(
                name: "precos_planos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanoId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrecoMensal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PrecoFundador = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AlteradoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_precos_planos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_precos_planos_planos_PlanoId",
                        column: x => x.PlanoId,
                        principalTable: "planos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_planos_Codigo",
                table: "planos",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_precos_planos_PlanoId_VigenteDesde",
                table: "precos_planos",
                columns: new[] { "PlanoId", "VigenteDesde" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "precos_planos");

            migrationBuilder.DropTable(
                name: "vagas_fundador");

            migrationBuilder.DropTable(
                name: "planos");
        }
    }
}
