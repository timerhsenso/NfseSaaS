using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ConviteIndicacaoConfiguration : IEntityTypeConfiguration<ConviteIndicacao>
{
    public void Configure(EntityTypeBuilder<ConviteIndicacao> builder)
    {
        builder.ToTable("convites_indicacao");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Email)
            .HasMaxLength(256)
            .IsRequired();

        // Limite diário da conta e lista "convites enviados".
        builder.HasIndex(c => new { c.TenantId, c.CreatedAt });

        // "Este endereço já recebeu convite nos últimos 30 dias?" (de qualquer conta).
        builder.HasIndex(c => new { c.Email, c.CreatedAt });
    }
}
