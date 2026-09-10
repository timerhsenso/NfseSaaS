using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaContratoServicoEStatusContrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Colunas novas em contratos (não dependem de nada)
            migrationBuilder.AddColumn<DateOnly>(
                name: "DataFim",
                table: "contratos",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermitirAlterarValorNaEmissao",
                table: "contratos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "contratos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TipoCobranca",
                table: "contratos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 2) Tabela nova — precisa existir ANTES do INSERT de migração
            // de dados do passo 3.
            migrationBuilder.CreateTable(
                name: "contrato_servicos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantidade = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contrato_servicos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contrato_servicos_contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contrato_servicos_servicos_ServicoId",
                        column: x => x.ServicoId,
                        principalTable: "servicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contrato_servicos_ContratoId",
                table: "contrato_servicos",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_contrato_servicos_ServicoId",
                table: "contrato_servicos",
                column: "ServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_contrato_servicos_TenantId_ContratoId",
                table: "contrato_servicos",
                columns: new[] { "TenantId", "ContratoId" });

            // 3) Migração de dados — contratos.ServicoId/ValorAtual AINDA
            // existem aqui (só são dropados no passo 4), então dá pra
            // copiar pra dentro da tabela nova. 1 linha por contrato
            // existente, Quantidade=1 (equivalente ao que ele já tinha
            // antes das linhas existirem).
            migrationBuilder.Sql(@"
                INSERT INTO contrato_servicos (""Id"", ""TenantId"", ""ContratoId"", ""ServicoId"", ""Quantidade"", ""ValorUnitario"", ""CreatedAt"")
                SELECT gen_random_uuid(), ""TenantId"", ""Id"", ""ServicoId"", 1, ""ValorAtual"", now()
                FROM contratos;
            ");

            // 4) SÓ AGORA remove a coluna antiga de contratos — os dados
            // já foram copiados no passo 3.
            migrationBuilder.DropForeignKey(
                name: "FK_contratos_servicos_ServicoId",
                table: "contratos");

            migrationBuilder.DropIndex(
                name: "IX_contratos_ServicoId",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "ServicoId",
                table: "contratos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contrato_servicos");

            migrationBuilder.DropColumn(
                name: "DataFim",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "PermitirAlterarValorNaEmissao",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "contratos");

            migrationBuilder.DropColumn(
                name: "TipoCobranca",
                table: "contratos");

            migrationBuilder.AddColumn<Guid>(
                name: "ServicoId",
                table: "contratos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_contratos_ServicoId",
                table: "contratos",
                column: "ServicoId");

            migrationBuilder.AddForeignKey(
                name: "FK_contratos_servicos_ServicoId",
                table: "contratos",
                column: "ServicoId",
                principalTable: "servicos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}