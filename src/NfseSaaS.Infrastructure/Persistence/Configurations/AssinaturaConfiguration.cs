using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class AssinaturaConfiguration : IEntityTypeConfiguration<Assinatura>
{
    public void Configure(EntityTypeBuilder<Assinatura> builder)
    {
        builder.ToTable("assinaturas");
        builder.HasKey(a => a.Id);

        // Uma assinatura por conta.
        builder.HasIndex(a => a.TenantId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enum como inteiro, sem valor padrão no banco (o valor vem sempre do código).
        builder.Property(a => a.Status).IsRequired();
        builder.Property(a => a.Plano);
        builder.Property(a => a.PrecoMensal).HasColumnType("numeric(10,2)");

        builder.Property(a => a.AsaasClienteId).HasMaxLength(60);
        builder.Property(a => a.AsaasAssinaturaId).HasMaxLength(60);
        builder.Property(a => a.UrlFaturaPendente).HasMaxLength(300);
        builder.Property(a => a.ValorFaturaPendente).HasColumnType("numeric(10,2)");
        builder.Property(a => a.MotivoIsencao).HasMaxLength(300);

        // O webhook acha a assinatura por aqui.
        builder.HasIndex(a => a.AsaasAssinaturaId)
            .IsUnique()
            .HasFilter("\"AsaasAssinaturaId\" IS NOT NULL");
    }
}
