using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ExportacaoDadosConfiguration : IEntityTypeConfiguration<ExportacaoDados>
{
    public void Configure(EntityTypeBuilder<ExportacaoDados> builder)
    {
        builder.ToTable("exportacoes_dados");
        builder.HasKey(e => e.Id);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.Erro).HasMaxLength(300);

        // Lista da tela e limite diário por conta.
        builder.HasIndex(e => new { e.TenantId, e.CreatedAt });
        // Limpeza diária (Pronta vencida).
        builder.HasIndex(e => new { e.Status, e.ExpiraEm });
    }
}
