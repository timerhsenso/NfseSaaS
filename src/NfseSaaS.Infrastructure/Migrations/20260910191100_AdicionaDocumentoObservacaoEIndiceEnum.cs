using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaDocumentoObservacaoEIndiceEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backup do texto original ANTES de converter — depois da
            // conversão o texto livre ("IPCA", "SELIC + 2%", etc.) se
            // perde pra sempre. Serve pra revisar manualmente depois o
            // que caiu em "Outro" (99) — ver os SELECTs de conferência
            // sugeridos após rodar esta migration.
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS _backup_indice_reajuste_migracao (
                    origem varchar(30) NOT NULL,
                    registro_id uuid NOT NULL,
                    indice_original varchar(30)
                );
                INSERT INTO _backup_indice_reajuste_migracao (origem, registro_id, indice_original)
                SELECT 'contratos', ""Id"", ""IndiceReajuste"" FROM contratos WHERE ""IndiceReajuste"" IS NOT NULL AND TRIM(""IndiceReajuste"") <> '';
                INSERT INTO _backup_indice_reajuste_migracao (origem, registro_id, indice_original)
                SELECT 'reajustes_contrato', ""Id"", ""IndiceUsado"" FROM reajustes_contrato WHERE ""IndiceUsado"" IS NOT NULL AND TRIM(""IndiceUsado"") <> '';
            ");

            // USING com CASE — não dá pra deixar o Postgres converter
            // sozinho (texto livre não tem cast automático pra integer;
            // o AlterColumn<int> puro que o EF gerou quebraria na hora
            // com "column cannot be cast automatically to type integer",
            // e mesmo que não quebrasse, um cast puro nunca ia saber que
            // "IPCA" deveria virar 0 — isso é regra de negócio, não
            // conversão de tipo). NULL e string em branco viram NULL;
            // qualquer texto que não bate com os 6 índices conhecidos
            // vira 99 (Outro) — nunca perdido, fica no backup acima.
            migrationBuilder.Sql(@"
                ALTER TABLE reajustes_contrato ALTER COLUMN ""IndiceUsado"" TYPE integer USING (
                    CASE
                        WHEN ""IndiceUsado"" IS NULL OR TRIM(""IndiceUsado"") = '' THEN NULL
                        WHEN UPPER(TRIM(""IndiceUsado"")) = 'IPCA' THEN 0
                        WHEN UPPER(TRIM(""IndiceUsado"")) = 'IGPM' THEN 1
                        WHEN UPPER(TRIM(""IndiceUsado"")) = 'INCC' THEN 2
                        WHEN UPPER(TRIM(""IndiceUsado"")) = 'INPC' THEN 3
                        WHEN UPPER(TRIM(""IndiceUsado"")) = 'SELIC' THEN 4
                        WHEN UPPER(TRIM(""IndiceUsado"")) = 'CDI' THEN 5
                        ELSE 99
                    END
                );
                ALTER TABLE contratos ALTER COLUMN ""IndiceReajuste"" TYPE integer USING (
                    CASE
                        WHEN ""IndiceReajuste"" IS NULL OR TRIM(""IndiceReajuste"") = '' THEN NULL
                        WHEN UPPER(TRIM(""IndiceReajuste"")) = 'IPCA' THEN 0
                        WHEN UPPER(TRIM(""IndiceReajuste"")) = 'IGPM' THEN 1
                        WHEN UPPER(TRIM(""IndiceReajuste"")) = 'INCC' THEN 2
                        WHEN UPPER(TRIM(""IndiceReajuste"")) = 'INPC' THEN 3
                        WHEN UPPER(TRIM(""IndiceReajuste"")) = 'SELIC' THEN 4
                        WHEN UPPER(TRIM(""IndiceReajuste"")) = 'CDI' THEN 5
                        ELSE 99
                    END
                );
            ");

            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "contratos",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contrato_documentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeOriginal = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Extensao = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contrato_documentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contrato_documentos_contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contrato_documentos_ContratoId",
                table: "contrato_documentos",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_contrato_documentos_TenantId_ContratoId",
                table: "contrato_documentos",
                columns: new[] { "TenantId", "ContratoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contrato_documentos");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "contratos");

            migrationBuilder.AlterColumn<string>(
                name: "IndiceUsado",
                table: "reajustes_contrato",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IndiceReajuste",
                table: "contratos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}