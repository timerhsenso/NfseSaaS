using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

/// <summary>RC1: pedidos para sair das cópias do e-mail da nota (histórico e bloqueio).</summary>
public sealed class SaidaCopiaEmailConfiguration : IEntityTypeConfiguration<SaidaCopiaEmail>
{
    public void Configure(EntityTypeBuilder<SaidaCopiaEmail> builder)
    {
        builder.ToTable("saidas_copia_email");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Email).HasMaxLength(100).IsRequired();
        builder.Property(s => s.CodigoHash).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Status).HasDefaultValue(StatusSaidaCopia.Pendente);
        builder.Property(s => s.IpPedido).HasMaxLength(45);
        builder.Property(s => s.IpConfirmacao).HasMaxLength(45);

        builder.HasIndex(s => s.CodigoHash).IsUnique();
        // Histórico do cliente e limite de pedidos por dia.
        builder.HasIndex(s => new { s.TenantId, s.ClienteId, s.Email, s.CreatedAt });

        // Some junto com o cliente.
        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(s => s.ClienteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
