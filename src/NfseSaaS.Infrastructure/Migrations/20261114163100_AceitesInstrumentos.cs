using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AceitesInstrumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aceites_instrumentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PerfilUsuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Versao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HashTexto = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: true),
                    CnpjEmpresa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CertificadoImpressaoDigital = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Detalhes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AceitoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Origem = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RevogadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevogadoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    MotivoRevogacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aceites_instrumentos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aceites_instrumentos_conta_tipo_empresa",
                table: "aceites_instrumentos",
                columns: new[] { "TenantId", "Tipo", "EmpresaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aceites_instrumentos");
        }
    }
}
