using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class NotaMensalidadeConfiguration : IEntityTypeConfiguration<NotaMensalidade>
{
    public void Configure(EntityTypeBuilder<NotaMensalidade> builder)
    {
        builder.ToTable("notas_mensalidade");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.AsaasPagamentoId).HasMaxLength(60).IsRequired();
        builder.Property(n => n.Valor).HasColumnType("numeric(10,2)");
        builder.Property(n => n.Status).IsRequired();
        builder.Property(n => n.UltimoErro).HasMaxLength(500);

        // Um pagamento, uma nota.
        builder.HasIndex(n => n.AsaasPagamentoId).IsUnique();
        // Fila do job (Pendente/Erro) e lista por conta.
        builder.HasIndex(n => new { n.Status, n.CreatedAt });
        builder.HasIndex(n => n.TenantId);
    }
}
