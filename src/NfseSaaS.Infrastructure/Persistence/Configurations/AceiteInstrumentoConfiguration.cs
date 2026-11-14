using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

/// <summary>JUR1: aceites do DPA e das autorizações (ver AceiteInstrumento).</summary>
public sealed class AceiteInstrumentoConfiguration : IEntityTypeConfiguration<AceiteInstrumento>
{
    public void Configure(EntityTypeBuilder<AceiteInstrumento> builder)
    {
        builder.ToTable("aceites_instrumentos");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.UsuarioEmail).HasMaxLength(256).IsRequired();
        builder.Property(a => a.PerfilUsuario).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Tipo).HasConversion<int>();
        builder.Property(a => a.Versao).HasMaxLength(20).IsRequired();
        builder.Property(a => a.HashTexto).HasMaxLength(64).IsRequired();
        builder.Property(a => a.CnpjEmpresa).HasMaxLength(20);
        builder.Property(a => a.CertificadoImpressaoDigital).HasMaxLength(128);
        builder.Property(a => a.Detalhes).HasMaxLength(2000);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.UserAgent).HasMaxLength(500);
        builder.Property(a => a.Origem).HasMaxLength(30).IsRequired();
        builder.Property(a => a.MotivoRevogacao).HasMaxLength(300);

        builder.Ignore(a => a.Vigente);

        // "Esta conta/empresa tem a autorização X vigente?"
        builder.HasIndex(a => new { a.TenantId, a.Tipo, a.EmpresaId })
            .HasDatabaseName("IX_aceites_instrumentos_conta_tipo_empresa");
    }
}
