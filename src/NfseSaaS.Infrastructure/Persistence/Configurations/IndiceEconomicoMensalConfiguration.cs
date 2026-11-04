using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class IndiceEconomicoMensalConfiguration : IEntityTypeConfiguration<IndiceEconomicoMensal>
{
    public void Configure(EntityTypeBuilder<IndiceEconomicoMensal> builder)
    {
        builder.ToTable("indices_economicos_mensais");

        builder.HasKey(i => i.Id);

        // Sem HasDefaultValue em Indice/Fonte de propósito: Ipca é 0 (default
        // do CLR) e o EF deixaria de enviar a coluna no INSERT, gravando o
        // default do banco no lugar do IPCA.
        builder.Property(i => i.Indice).IsRequired();
        builder.Property(i => i.Fonte).IsRequired();

        builder.Property(i => i.Variacao)
            .HasPrecision(9, 4)
            .IsRequired();

        builder.HasIndex(i => new { i.Indice, i.Competencia }).IsUnique();
    }
}
