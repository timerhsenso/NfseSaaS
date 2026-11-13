using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DocumentosEscolhidosEm",
                table: "empresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pacotes_documento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TiposDocumentoIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pacotes_documento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tipos_documento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1000', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Categoria = table.Column<int>(type: "integer", nullable: false, defaultValue: 99),
                    Abrangencia = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Orgao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LinkEmissao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValidadeDias = table.Column<int>(type: "integer", nullable: true),
                    ExigeValidade = table.Column<bool>(type: "boolean", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PadraoNovasEmpresas = table.Column<bool>(type: "boolean", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tipos_documento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documentos_gerenciados_empresa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoDocumentoId = table.Column<int>(type: "integer", nullable: false),
                    LinkProprio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentos_gerenciados_empresa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documentos_gerenciados_empresa_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_documentos_gerenciados_empresa_tipos_documento_TipoDocument~",
                        column: x => x.TipoDocumentoId,
                        principalTable: "tipos_documento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "links_locais_documento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoDocumentoId = table.Column<int>(type: "integer", nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    CodigoMunicipio = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_links_locais_documento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_links_locais_documento_tipos_documento_TipoDocumentoId",
                        column: x => x.TipoDocumentoId,
                        principalTable: "tipos_documento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "tipos_documento",
                columns: new[] { "Id", "Abrangencia", "Ativo", "Categoria", "Codigo", "CreatedAt", "ExigeValidade", "LinkEmissao", "Nome", "Ordem", "Orgao", "PadraoNovasEmpresas", "UpdatedAt", "ValidadeDias" },
                values: new object[,]
                {
                    { 1, 1, true, 1, "cnd-federal", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "https://servicos.receitafederal.gov.br/servico/certidoes/#/home", "Certidão federal (Receita/PGFN)", 10, "Receita Federal", true, null, 180 },
                    { 2, 2, true, 1, "cnd-estadual", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, null, "Certidão estadual", 20, "SEFAZ do estado", true, null, null },
                    { 3, 3, true, 1, "cnd-municipal", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, null, "Certidão municipal", 30, "Prefeitura", true, null, null },
                    { 4, 1, true, 2, "crf-fgts", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "https://consulta-crf.caixa.gov.br/consultacrf/pages/consultaEmpregador.jsf", "Regularidade do FGTS (CRF)", 10, "Caixa", true, null, 30 },
                    { 5, 1, true, 2, "cndt", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "https://cndt-certidao.tst.jus.br/", "Certidão trabalhista (CNDT)", 20, "TST", true, null, 180 },
                    { 6, 1, true, 1, "simples-nacional", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "https://www8.receita.fazenda.gov.br/SimplesNacional/aplicacoes.aspx?id=21", "Opção pelo Simples Nacional", 40, "Receita Federal", true, null, null },
                    { 7, 2, true, 3, "contrato-social", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, null, "Contrato social", 20, "Junta Comercial", true, null, null },
                    { 8, 1, true, 3, "cartao-cnpj", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "https://solucoes.receita.fazenda.gov.br/Servicos/cnpjreva/Cnpjreva_Solicitacao.asp", "Cartão CNPJ", 10, "Receita Federal", true, null, null },
                    { 99, 1, true, 99, "outro", new DateTimeOffset(new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, null, "Outro documento", 999, null, false, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_documentos_empresa_Tipo",
                table: "documentos_empresa",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_documentos_gerenciados_empresa_EmpresaId_TipoDocumentoId",
                table: "documentos_gerenciados_empresa",
                columns: new[] { "EmpresaId", "TipoDocumentoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documentos_gerenciados_empresa_TipoDocumentoId",
                table: "documentos_gerenciados_empresa",
                column: "TipoDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_links_locais_documento_TipoDocumentoId_Uf_CodigoMunicipio",
                table: "links_locais_documento",
                columns: new[] { "TipoDocumentoId", "Uf", "CodigoMunicipio" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_pacotes_documento_Codigo",
                table: "pacotes_documento",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tipos_documento_Codigo",
                table: "tipos_documento",
                column: "Codigo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_documentos_empresa_tipos_documento_Tipo",
                table: "documentos_empresa",
                column: "Tipo",
                principalTable: "tipos_documento",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_documentos_empresa_tipos_documento_Tipo",
                table: "documentos_empresa");

            migrationBuilder.DropTable(
                name: "documentos_gerenciados_empresa");

            migrationBuilder.DropTable(
                name: "links_locais_documento");

            migrationBuilder.DropTable(
                name: "pacotes_documento");

            migrationBuilder.DropTable(
                name: "tipos_documento");

            migrationBuilder.DropIndex(
                name: "IX_documentos_empresa_Tipo",
                table: "documentos_empresa");

            migrationBuilder.DropColumn(
                name: "DocumentosEscolhidosEm",
                table: "empresas");
        }
    }
}
