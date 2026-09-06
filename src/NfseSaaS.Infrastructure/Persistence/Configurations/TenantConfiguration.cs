using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Cnpj)
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(t => t.RazaoSocial)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.NomeFantasia)
            .HasMaxLength(200);

        builder.HasIndex(t => t.Cnpj)
            .IsUnique();
    }
}
