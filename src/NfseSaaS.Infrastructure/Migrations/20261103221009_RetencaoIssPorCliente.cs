using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetencaoIssPorCliente : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Ordem ajustada à mão (o EF gerou o DropColumn primeiro): a coluna
        /// empresas.TpRetIssqn precisa existir até o UPDATE que passa a
        /// retenção para os Clientes — senão a informação se perde.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Colunas novas.
            migrationBuilder.AddColumn<bool>(
                name: "RetemIss",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AliquotaIss",
                table: "empresas",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AliquotaIss",
                table: "notas_fiscais",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            // 2) Dados: Clientes de Empresas que estavam com "Retido pelo
            //    Tomador" (TpRetIssqn = '2') herdam a retenção.
            migrationBuilder.Sql(@"
                UPDATE clientes c
                   SET ""RetemIss"" = TRUE
                  FROM empresas e
                 WHERE c.""EmpresaId"" = e.""Id""
                   AND e.""TpRetIssqn"" = '2';");

            // 3) Só agora a coluna antiga sai.
            migrationBuilder.DropColumn(
                name: "TpRetIssqn",
                table: "empresas");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Rollback: TpRetIssqn volta com '1' (Não retido), valor válido no
        /// XSD — o padrão "" gerado pelo EF deixaria toda DPS inválida. A
        /// retenção por Cliente não tem como voltar para a Empresa (vários
        /// Clientes, um valor só), então ela se perde no rollback.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TpRetIssqn",
                table: "empresas",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "1");

            migrationBuilder.DropColumn(
                name: "AliquotaIss",
                table: "notas_fiscais");

            migrationBuilder.DropColumn(
                name: "AliquotaIss",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "RetemIss",
                table: "clientes");
        }
    }
}