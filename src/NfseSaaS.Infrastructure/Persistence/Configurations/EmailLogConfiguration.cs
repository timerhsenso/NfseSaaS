using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("email_logs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Tipo)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Destinatario)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Assunto)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.CorpoHtml)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(e => e.ErroDetalhe)
            .HasMaxLength(2000);

        builder.HasIndex(e => new { e.TenantId, e.Status, e.CreatedAt });
    }
}
