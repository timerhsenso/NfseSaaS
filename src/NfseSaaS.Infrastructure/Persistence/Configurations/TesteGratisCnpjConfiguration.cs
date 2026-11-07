using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class TesteGratisCnpjConfiguration : IEntityTypeConfiguration<TesteGratisCnpj>
{
    public void Configure(EntityTypeBuilder<TesteGratisCnpj> builder)
    {
        builder.ToTable("testes_gratis_cnpj");

        // A chave é o próprio CNPJ: um teste por CNPJ, para sempre.
        builder.HasKey(t => t.Cnpj);
        builder.Property(t => t.Cnpj).HasMaxLength(14);
        builder.Property(t => t.IniciadoEm).IsRequired();

        // Sem FK para tenants (a linha sobrevive à exclusão da conta).
        builder.HasIndex(t => t.TenantId);
    }
}
