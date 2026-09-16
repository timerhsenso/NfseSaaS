using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class CodigoTributacaoNacionalConfiguration : IEntityTypeConfiguration<CodigoTributacaoNacional>
{
    public void Configure(EntityTypeBuilder<CodigoTributacaoNacional> builder)
    {
        builder.ToTable("codigos_tributacao_nacional");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Codigo)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.Descricao)
            .HasMaxLength(500)
            .IsRequired();

        // Único por código — o seeder confia nisso pra checar duplicata
        // (ver CodigoTributacaoNacionalSeeder), e a validação de Servico
        // busca por esta coluna.
        builder.HasIndex(c => c.Codigo).IsUnique();
    }
}
