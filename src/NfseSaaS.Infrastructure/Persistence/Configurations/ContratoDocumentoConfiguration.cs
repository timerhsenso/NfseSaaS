using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ContratoDocumentoConfiguration : IEntityTypeConfiguration<ContratoDocumento>
{
    public void Configure(EntityTypeBuilder<ContratoDocumento> builder)
    {
        builder.ToTable("contrato_documentos");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.NomeOriginal)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(d => d.Extensao)
            .HasMaxLength(10)
            .IsRequired();

        builder.HasIndex(d => new { d.TenantId, d.ContratoId });

        // Linha de detalhe do Contrato: some junto quando o Contrato é
        // excluído (mesmo padrão de ContratoServico) — o arquivo físico
        // correspondente é apagado explicitamente em
        // ExcluirContratoUseCase antes disso, via
        // ContratoDocumentoFileStore.ExcluirPastaContrato.
        builder.HasOne<Contrato>()
            .WithMany()
            .HasForeignKey(d => d.ContratoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
