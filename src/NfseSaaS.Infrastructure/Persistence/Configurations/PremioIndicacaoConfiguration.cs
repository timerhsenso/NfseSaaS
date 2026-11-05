using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class PremioIndicacaoConfiguration : IEntityTypeConfiguration<PremioIndicacao>
{
    public void Configure(EntityTypeBuilder<PremioIndicacao> builder)
    {
        builder.ToTable("premios_indicacao");
        builder.HasKey(p => p.Id);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.IndicadoTenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enums como inteiro, sem valor padrão no banco (o valor vem sempre do código).
        builder.Property(p => p.Papel).IsRequired();
        builder.Property(p => p.Status).IsRequired();

        builder.Property(p => p.AsaasPagamentoOrigemId).HasMaxLength(60);
        builder.Property(p => p.AsaasPagamentoDispensadoId).HasMaxLength(60);
        builder.Property(p => p.Detalhe).HasMaxLength(300);

        // Cada indicação premia cada lado uma única vez.
        builder.HasIndex(p => new { p.IndicadoTenantId, p.Papel }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => p.Status);
    }
}
