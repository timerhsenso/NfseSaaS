using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class EventoCobrancaConfiguration : IEntityTypeConfiguration<EventoCobranca>
{
    public void Configure(EntityTypeBuilder<EventoCobranca> builder)
    {
        builder.ToTable("eventos_cobranca");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventoId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Tipo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.AsaasPagamentoId).HasMaxLength(60);
        builder.Property(e => e.AsaasAssinaturaId).HasMaxLength(60);
        builder.Property(e => e.Payload).HasColumnType("text").IsRequired();
        builder.Property(e => e.Resultado).HasMaxLength(200);

        // Idempotência: o mesmo evento do Asaas só é gravado uma vez.
        builder.HasIndex(e => e.EventoId).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.CreatedAt });
    }
}
