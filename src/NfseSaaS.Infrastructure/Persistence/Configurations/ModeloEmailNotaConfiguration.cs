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

        builder.Property(m => m.Nome).HasMaxLength(ModeloEmailNota.TamanhoMaximoNome).IsRequired();
        builder.Property(m => m.Assunto).HasMaxLength(ModeloEmailNota.TamanhoMaximoAssunto).IsRequired();
        builder.Property(m => m.Mensagem).HasMaxLength(ModeloEmailNota.TamanhoMaximoMensagem).IsRequired();
        builder.Property(m => m.ResponderPara).HasMaxLength(ModeloEmailNota.TamanhoMaximoEmail);
        builder.Property(m => m.CopiaOcultaPara).HasMaxLength(ModeloEmailNota.TamanhoMaximoEmail);
        // CR1: modelos que já existem ganham os 30 dias.
        builder.Property(m => m.ValidadeConfirmacaoDias).HasDefaultValue(ModeloEmailNota.ValidadeConfirmacaoPadraoDias);

        // Nome único na conta e um só padrão por conta.
        builder.HasIndex(m => new { m.TenantId, m.Nome }).IsUnique();
        builder.HasIndex(m => m.TenantId).IsUnique().HasFilter("\"Padrao\"");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
