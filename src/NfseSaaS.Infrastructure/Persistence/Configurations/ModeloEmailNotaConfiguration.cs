using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ModeloEmailNotaConfiguration : IEntityTypeConfiguration<ModeloEmailNota>
{
    public void Configure(EntityTypeBuilder<ModeloEmailNota> builder)
    {
        builder.ToTable("modelos_email_nota");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Assunto).HasMaxLength(ModeloEmailNota.TamanhoMaximoAssunto).IsRequired();
        builder.Property(m => m.Mensagem).HasMaxLength(ModeloEmailNota.TamanhoMaximoMensagem).IsRequired();
        builder.Property(m => m.ResponderPara).HasMaxLength(ModeloEmailNota.TamanhoMaximoEmail);
        builder.Property(m => m.CopiaOcultaPara).HasMaxLength(ModeloEmailNota.TamanhoMaximoEmail);

        // Um modelo da conta e no máximo um por empresa (mesmo padrão dos
        // índices parciais do Cliente compartilhado).
        builder.HasIndex(m => m.TenantId).IsUnique().HasFilter("\"EmpresaId\" IS NULL");
        builder.HasIndex(m => new { m.TenantId, m.EmpresaId }).IsUnique().HasFilter("\"EmpresaId\" IS NOT NULL");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configuração, não histórico: sai junto com a empresa.
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(m => m.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
