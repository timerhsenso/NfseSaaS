using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class MunicipioConfiguration : IEntityTypeConfiguration<Municipio>
{
    public void Configure(EntityTypeBuilder<Municipio> builder)
    {
        builder.ToTable("municipios");

        builder.HasKey(m => m.Codigo);
        builder.Property(m => m.Codigo).HasMaxLength(7).IsRequired();
        builder.Property(m => m.Nome).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Uf).HasMaxLength(2).IsRequired();
        builder.Property(m => m.NomeBusca).HasMaxLength(100).IsRequired();

        // Busca por nome (com ou sem UF) na tela de Cliente e de Empresa.
        builder.HasIndex(m => new { m.Uf, m.NomeBusca });
        builder.HasIndex(m => m.NomeBusca);
    }
}
