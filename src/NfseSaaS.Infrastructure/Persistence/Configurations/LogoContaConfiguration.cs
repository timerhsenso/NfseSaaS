using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class LogoContaConfiguration : IEntityTypeConfiguration<LogoConta>
{
    public void Configure(EntityTypeBuilder<LogoConta> builder)
    {
        builder.ToTable("logos_conta");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Conteudo).IsRequired();
        builder.Property(l => l.TipoConteudo).HasMaxLength(20).IsRequired();
        builder.Property(l => l.Versao).HasMaxLength(16).IsRequired();

        // Uma por conta.
        builder.HasIndex(l => l.TenantId).IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(l => l.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
