using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class NfseConfiguration : IEntityTypeConfiguration<Nfse>
{
    public void Configure(EntityTypeBuilder<Nfse> builder)
    {
        builder.ToTable("notas_fiscais");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.SerieDps)
            .HasMaxLength(5)
            .IsRequired();

        builder.Property(n => n.NumeroNfse)
            .HasMaxLength(50);

        builder.Property(n => n.ChaveAcesso)
            .HasMaxLength(60);

        builder.Property(n => n.ValorServico)
            .HasColumnType("numeric(18,2)");

        builder.Property(n => n.DescricaoServico)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(n => n.CodigoErro)
            .HasMaxLength(20);

        builder.Property(n => n.MensagemErro)
            .HasMaxLength(1000);

        // Não pode existir duas DPS com o mesmo número+série para a mesma empresa/tenant.
        builder.HasIndex(n => new { n.TenantId, n.EmpresaId, n.NumeroDps, n.SerieDps })
            .IsUnique();

        builder.HasIndex(n => n.ChaveAcesso)
            .IsUnique()
            .HasFilter("\"ChaveAcesso\" IS NOT NULL");
    }
}
