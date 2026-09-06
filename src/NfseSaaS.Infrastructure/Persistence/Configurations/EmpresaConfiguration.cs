using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class EmpresaConfiguration : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> builder)
    {
        builder.ToTable("empresas");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Cnpj)
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(e => e.RazaoSocial)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.InscricaoMunicipal)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.CodigoMunicipio)
            .HasMaxLength(7)
            .IsRequired();

        // Um mesmo CNPJ não pode se repetir dentro do mesmo tenant.
        builder.HasIndex(e => new { e.TenantId, e.Cnpj })
            .IsUnique();
    }
}
