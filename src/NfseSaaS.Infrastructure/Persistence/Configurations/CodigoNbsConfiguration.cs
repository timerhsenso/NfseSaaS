using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class CodigoNbsConfiguration : IEntityTypeConfiguration<CodigoNbs>
{
    public void Configure(EntityTypeBuilder<CodigoNbs> builder)
    {
        builder.ToTable("codigos_nbs");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Codigo)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.Descricao)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(c => c.Codigo).IsUnique();
    }
}
