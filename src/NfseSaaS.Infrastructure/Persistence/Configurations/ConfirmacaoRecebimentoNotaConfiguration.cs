using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

/// <summary>CR1: confirmações de recebimento do e-mail da nota.</summary>
public sealed class ConfirmacaoRecebimentoNotaConfiguration : IEntityTypeConfiguration<ConfirmacaoRecebimentoNota>
{
    public void Configure(EntityTypeBuilder<ConfirmacaoRecebimentoNota> builder)
    {
        builder.ToTable("confirmacoes_recebimento_nota");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Email).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Ip).HasMaxLength(45);

        // Uma confirmação por e-mail e nota (confirmar de novo não duplica).
        builder.HasIndex(c => new { c.NfseId, c.Email }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.NfseId });

        // Some junto com a nota.
        builder.HasOne<Nfse>()
            .WithMany()
            .HasForeignKey(c => c.NfseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
