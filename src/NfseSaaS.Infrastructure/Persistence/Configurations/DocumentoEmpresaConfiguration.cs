using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

/// <summary>DOC1: documentos da Empresa (uma linha por versão enviada).</summary>
public sealed class DocumentoEmpresaConfiguration : IEntityTypeConfiguration<DocumentoEmpresa>
{
    public void Configure(EntityTypeBuilder<DocumentoEmpresa> builder)
    {
        builder.ToTable("documentos_empresa");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.NomePersonalizado).HasMaxLength(80);
        builder.Property(d => d.NomeChave).HasMaxLength(80).IsRequired();
        builder.Property(d => d.NomeArquivo).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Observacao).HasMaxLength(300);

        // Lista da tela e histórico: versões de um "lugar", da mais nova para a mais antiga.
        builder.HasIndex(d => new { d.TenantId, d.EmpresaId, d.Tipo, d.NomeChave, d.CreatedAt });

        // Some junto com a Empresa (a pasta em disco é apagada em ExcluirEmpresaUseCase).
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(d => d.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
