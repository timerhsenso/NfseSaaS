using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Operacao)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.Entidade)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45);

        builder.HasIndex(a => new { a.TenantId, a.DataHora });
    }
}
