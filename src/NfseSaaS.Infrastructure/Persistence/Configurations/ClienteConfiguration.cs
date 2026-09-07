using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CpfCnpj)
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(c => c.Nome)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Email)
            .HasMaxLength(200);

        builder.Property(c => c.Telefone)
            .HasMaxLength(20);

        builder.Property(c => c.CodigoMunicipio)
            .HasMaxLength(7);

        builder.Property(c => c.Cep)
            .HasMaxLength(8);

        builder.Property(c => c.Logradouro)
            .HasMaxLength(200);

        builder.Property(c => c.Numero)
            .HasMaxLength(20);

        builder.Property(c => c.Bairro)
            .HasMaxLength(100);

        builder.HasIndex(c => new { c.TenantId, c.CpfCnpj });
    }
}
