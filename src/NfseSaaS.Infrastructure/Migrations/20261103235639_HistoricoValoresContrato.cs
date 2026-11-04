using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HistoricoValoresContrato : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Escrita à mão: o EF gerou DropTable(reajustes_contrato) +
        /// CreateTable, o que APAGARIA os reajustes já registrados. Aqui a
        /// tabela nova é criada com os mesmos nomes que o EF espera
        /// (PK/FK/índices), os dados são copiados, e só então a antiga sai.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Tabela nova (idêntica ao que o EF gerou).
            migrationBuilder.CreateTable(
                name: "movimentacoes_valor_contrato",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    DataVigencia = table.Column<DateOnly>(type: "date", nullable: false),
                    ValorAnterior = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ValorNovo = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PercentualCalculado = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    PercentualAplicado = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    Indice = table.Column<int>(type: "integer", nullable: true),
                    CompetenciaIndiceInicio = table.Column<DateOnly>(type: "date", nullable: true),
                    CompetenciaIndiceFim = table.Column<DateOnly>(type: "date", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ComposicaoJson = table.Column<string>(type: "jsonb", nullable: true),
                    Automatico = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DecididoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecididoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimentacoes_valor_contrato", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movimentacoes_valor_contrato_contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_valor_contrato_ContratoId",
                table: "movimentacoes_valor_contrato",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_valor_contrato_TenantId_ContratoId_DataVigenc~",
                table: "movimentacoes_valor_contrato",
                columns: new[] { "TenantId", "ContratoId", "DataVigencia" });

            // 2) Reajustes já registrados viram eventos Tipo=Reajuste (2),
            //    Status=Aplicado (1) — mesmo Id e mesma data de criação.
            migrationBuilder.Sql(@"
                INSERT INTO movimentacoes_valor_contrato
                    (""Id"", ""TenantId"", ""ContratoId"", ""Tipo"", ""Status"", ""DataVigencia"",
                     ""ValorAnterior"", ""ValorNovo"", ""PercentualAplicado"", ""Indice"", ""Motivo"",
                     ""Automatico"", ""CreatedAt"", ""UpdatedAt"")
                SELECT r.""Id"", r.""TenantId"", r.""ContratoId"", 2, 1, r.""DataReajuste"",
                       r.""ValorAnterior"", r.""ValorNovo"", r.""PercentualAplicado"", r.""IndiceUsado"", r.""Observacao"",
                       FALSE, r.""CreatedAt"", r.""UpdatedAt""
                  FROM reajustes_contrato r;");

            // 3) Valor inicial de cada Contrato (Tipo=1): o valor ANTES do
            //    primeiro reajuste, ou o valor atual se nunca houve reajuste.
            //    Composição só é conhecida com certeza quando não houve
            //    reajuste (as linhas atuais são as originais); senão fica nula.
            migrationBuilder.Sql(@"
                INSERT INTO movimentacoes_valor_contrato
                    (""Id"", ""TenantId"", ""ContratoId"", ""Tipo"", ""Status"", ""DataVigencia"",
                     ""ValorAnterior"", ""ValorNovo"", ""ComposicaoJson"", ""Automatico"", ""CreatedAt"")
                SELECT gen_random_uuid(), c.""TenantId"", c.""Id"", 1, 1, c.""DataInicioContrato"",
                       NULL,
                       COALESCE(primeiro.""ValorAnterior"", c.""ValorAtual""),
                       CASE WHEN primeiro.""Id"" IS NULL THEN comp.linhas END,
                       FALSE, c.""CreatedAt""
                  FROM contratos c
                  LEFT JOIN LATERAL (
                        SELECT r.""Id"", r.""ValorAnterior""
                          FROM reajustes_contrato r
                         WHERE r.""ContratoId"" = c.""Id""
                         ORDER BY r.""DataReajuste"", r.""CreatedAt""
                         LIMIT 1) primeiro ON TRUE
                  LEFT JOIN LATERAL (
                        SELECT jsonb_agg(jsonb_build_object(
                                   'servicoId', cs.""ServicoId"",
                                   'servicoDescricao', s.""Descricao"",
                                   'quantidade', cs.""Quantidade"",
                                   'valorUnitario', cs.""ValorUnitario"") ORDER BY s.""Descricao"") AS linhas
                          FROM contrato_servicos cs
                          JOIN servicos s ON s.""Id"" = cs.""ServicoId""
                         WHERE cs.""ContratoId"" = c.""Id"") comp ON TRUE;");

            // 4) O reajuste MAIS RECENTE de cada Contrato ganha a composição
            //    atual (as linhas de hoje são exatamente as que ele deixou).
            migrationBuilder.Sql(@"
                UPDATE movimentacoes_valor_contrato m
                   SET ""ComposicaoJson"" = comp.linhas
                  FROM (SELECT DISTINCT ON (r.""ContratoId"") r.""Id"", r.""ContratoId""
                          FROM movimentacoes_valor_contrato r
                         WHERE r.""Tipo"" = 2
                         ORDER BY r.""ContratoId"", r.""DataVigencia"" DESC, r.""CreatedAt"" DESC) ultimo
                  JOIN LATERAL (
                        SELECT jsonb_agg(jsonb_build_object(
                                   'servicoId', cs.""ServicoId"",
                                   'servicoDescricao', s.""Descricao"",
                                   'quantidade', cs.""Quantidade"",
                                   'valorUnitario', cs.""ValorUnitario"") ORDER BY s.""Descricao"") AS linhas
                          FROM contrato_servicos cs
                          JOIN servicos s ON s.""Id"" = cs.""ServicoId""
                         WHERE cs.""ContratoId"" = ultimo.""ContratoId"") comp ON TRUE
                 WHERE m.""Id"" = ultimo.""Id"";");

            // 5) Só agora a tabela antiga sai.
            migrationBuilder.DropTable(name: "reajustes_contrato");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Rollback: recria reajustes_contrato e devolve só os eventos de
        /// Reajuste APLICADOS (Valor inicial e Alteração de escopo não
        /// existiam no modelo antigo e se perdem).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reajustes_contrato",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataReajuste = table.Column<DateOnly>(type: "date", nullable: false),
                    ValorAnterior = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ValorNovo = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PercentualAplicado = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    IndiceUsado = table.Column<int>(type: "integer", nullable: true),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reajustes_contrato", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reajustes_contrato_contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_contrato_ContratoId",
                table: "reajustes_contrato",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_contrato_TenantId_ContratoId",
                table: "reajustes_contrato",
                columns: new[] { "TenantId", "ContratoId" });

            migrationBuilder.Sql(@"
                INSERT INTO reajustes_contrato
                    (""Id"", ""TenantId"", ""ContratoId"", ""DataReajuste"", ""ValorAnterior"", ""ValorNovo"",
                     ""PercentualAplicado"", ""IndiceUsado"", ""Observacao"", ""CreatedAt"", ""UpdatedAt"")
                SELECT m.""Id"", m.""TenantId"", m.""ContratoId"", m.""DataVigencia"", COALESCE(m.""ValorAnterior"", 0), m.""ValorNovo"",
                       m.""PercentualAplicado"", m.""Indice"", m.""Motivo"", m.""CreatedAt"", m.""UpdatedAt""
                  FROM movimentacoes_valor_contrato m
                 WHERE m.""Tipo"" = 2 AND m.""Status"" = 1;");

            migrationBuilder.DropTable(name: "movimentacoes_valor_contrato");
        }
    }
}