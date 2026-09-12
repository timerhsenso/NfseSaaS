using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaModuloSegurancaIaec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GrupoId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "grupos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Padrao = table.Column<bool>(type: "boolean", nullable: false),
                    EhAdministrador = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grupos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "telas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "grupo_telas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Incluir = table.Column<bool>(type: "boolean", nullable: false),
                    Alterar = table.Column<bool>(type: "boolean", nullable: false),
                    Excluir = table.Column<bool>(type: "boolean", nullable: false),
                    Consultar = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grupo_telas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grupo_telas_grupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "grupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_grupo_telas_telas_TelaId",
                        column: x => x.TelaId,
                        principalTable: "telas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_GrupoId",
                table: "AspNetUsers",
                column: "GrupoId");

            migrationBuilder.CreateIndex(
                name: "IX_grupo_telas_GrupoId",
                table: "grupo_telas",
                column: "GrupoId");

            migrationBuilder.CreateIndex(
                name: "IX_grupo_telas_TelaId",
                table: "grupo_telas",
                column: "TelaId");

            migrationBuilder.CreateIndex(
                name: "IX_grupo_telas_TenantId_GrupoId_TelaId",
                table: "grupo_telas",
                columns: new[] { "TenantId", "GrupoId", "TelaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grupos_TenantId_Nome",
                table: "grupos",
                columns: new[] { "TenantId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telas_Codigo",
                table: "telas",
                column: "Codigo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_grupos_GrupoId",
                table: "AspNetUsers",
                column: "GrupoId",
                principalTable: "grupos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_grupos_GrupoId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "grupo_telas");

            migrationBuilder.DropTable(
                name: "grupos");

            migrationBuilder.DropTable(
                name: "telas");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_GrupoId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GrupoId",
                table: "AspNetUsers");
        }
    }
}
