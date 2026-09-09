using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class NfseEventoConfiguration : IEntityTypeConfiguration<NfseEvento>
{
    public void Configure(EntityTypeBuilder<NfseEvento> builder)
    {
        builder.ToTable("nfse_eventos");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Tipo)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(e => e.Codigo)
            .HasMaxLength(20);

        builder.Property(e => e.Mensagem)
            .HasMaxLength(1000);

        // Consulta principal: histórico de uma Nfse em ordem cronológica.
        builder.HasIndex(e => new { e.TenantId, e.NfseId, e.CreatedAt });

        // FK real — ver comentário completo em ClienteConfiguration. Não
        // há hoje (nem está previsto) exclusão física de Nfse, então
        // Restrict aqui é só o mesmo backstop de banco usado em toda
        // entidade tenant-scoped do projeto, não uma restrição ativa.
        builder.HasOne<Nfse>()
            .WithMany()
            .HasForeignKey(e => e.NfseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
