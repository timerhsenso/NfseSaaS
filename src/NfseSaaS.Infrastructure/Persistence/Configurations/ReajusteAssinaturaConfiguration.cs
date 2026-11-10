using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ReajusteAssinaturaConfiguration : IEntityTypeConfiguration<ReajusteAssinatura>
{
    public void Configure(EntityTypeBuilder<ReajusteAssinatura> builder)
    {
        builder.ToTable("reajustes_assinatura");
        builder.HasKey(r => r.Id);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Assinatura>()
            .WithMany()
            .HasForeignKey(r => r.AssinaturaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enum como inteiro, sem valor padrão no banco (o valor vem sempre do código).
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.PrecoAnterior).HasColumnType("numeric(10,2)");
        builder.Property(r => r.PrecoNovo).HasColumnType("numeric(10,2)");
        builder.Property(r => r.Motivo).HasMaxLength(200);
        builder.Property(r => r.Detalhe).HasMaxLength(300);

        // O job procura os agendados vencidos; a tela, por lote e por conta.
        builder.HasIndex(r => new { r.Status, r.AplicarEm });
        builder.HasIndex(r => r.LoteId);
        builder.HasIndex(r => new { r.TenantId, r.Status });

        // No máximo um reajuste agendado por assinatura (backstop da regra).
        builder.HasIndex(r => r.AssinaturaId)
            .IsUnique()
            .HasFilter("\"Status\" = 1");
    }
}
