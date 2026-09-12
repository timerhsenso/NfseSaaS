using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class TelaConfiguration : IEntityTypeConfiguration<Tela>
{
    public void Configure(EntityTypeBuilder<Tela> builder)
    {
        builder.ToTable("telas");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Codigo)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Nome)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(t => t.Codigo)
            .IsUnique();
    }
}
