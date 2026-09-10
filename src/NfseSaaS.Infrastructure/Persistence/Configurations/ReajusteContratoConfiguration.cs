using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ReajusteContratoConfiguration : IEntityTypeConfiguration<ReajusteContrato>
{
    public void Configure(EntityTypeBuilder<ReajusteContrato> builder)
    {
        builder.ToTable("reajustes_contrato");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ValorAnterior).HasColumnType("numeric(18,2)");
        builder.Property(r => r.ValorNovo).HasColumnType("numeric(18,2)");
        builder.Property(r => r.PercentualAplicado).HasColumnType("numeric(9,4)");
        builder.Property(r => r.IndiceUsado).HasMaxLength(30);
        builder.Property(r => r.Observacao).HasMaxLength(500);

        builder.HasIndex(r => new { r.TenantId, r.ContratoId });

        // FK real (sem navigation property, mesmo padrão do resto do
        // projeto). Restrict: um Contrato com histórico de reajuste não
        // pode ser excluído (ver ExcluirContratoUseCase) — histórico é
        // append-only, apagar o Contrato apagaria o rastro que a Parte 3
        // existe justamente pra preservar.
        builder.HasOne<Contrato>()
            .WithMany()
            .HasForeignKey(r => r.ContratoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
