using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NfseSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndicacaoDeContas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoIndicacao",
                table: "tenants",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IndicadoPorTenantId",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemCadastro",
                table: "tenants",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_CodigoIndicacao",
                table: "tenants",
                column: "CodigoIndicacao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_IndicadoPorTenantId",
                table: "tenants",
                column: "IndicadoPorTenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_tenants_tenants_IndicadoPorTenantId",
                table: "tenants",
                column: "IndicadoPorTenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tenants_tenants_IndicadoPorTenantId",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_tenants_CodigoIndicacao",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_tenants_IndicadoPorTenantId",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "CodigoIndicacao",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "IndicadoPorTenantId",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "OrigemCadastro",
                table: "tenants");
        }
    }
}
