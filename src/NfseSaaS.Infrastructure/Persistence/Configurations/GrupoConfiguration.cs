using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class GrupoConfiguration : IEntityTypeConfiguration<Grupo>
{
    public void Configure(EntityTypeBuilder<Grupo> builder)
    {
        builder.ToTable("grupos");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Nome)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(g => g.Descricao)
            .HasMaxLength(500);

        builder.HasIndex(g => new { g.TenantId, g.Nome })
            .IsUnique();
    }
}
