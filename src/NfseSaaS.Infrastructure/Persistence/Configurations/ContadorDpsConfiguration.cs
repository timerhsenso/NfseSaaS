using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ContadorDpsConfiguration : IEntityTypeConfiguration<ContadorDps>
{
    public void Configure(EntityTypeBuilder<ContadorDps> builder)
    {
        builder.ToTable("contadores_dps");

        // Chave composta: um contador por Empresa+Série. EmpresaId já é
        // suficiente para isolar por Tenant (Empresa é tenant-scoped e seu
        // Id é único globalmente), então não há necessidade de TenantId
        // aqui nem de Global Query Filter.
        builder.HasKey(c => new { c.EmpresaId, c.SerieDps });

        builder.Property(c => c.SerieDps)
            .HasMaxLength(5)
            .IsRequired();

        builder.Property(c => c.UltimoNumero)
            .IsRequired();
    }
}
