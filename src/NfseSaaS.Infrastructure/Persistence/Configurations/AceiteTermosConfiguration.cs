using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class AceiteTermosConfiguration : IEntityTypeConfiguration<AceiteTermos>
{
    public void Configure(EntityTypeBuilder<AceiteTermos> builder)
    {
        builder.ToTable("aceites_termos");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.VersaoTermosDeUso)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.VersaoPoliticaPrivacidade)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45);

        builder.Property(a => a.UserAgent)
            .HasMaxLength(500);

        builder.Property(a => a.Origem)
            .HasMaxLength(30)
            .IsRequired();

        // Consulta da tela de aceite: "este usuário já aceitou estas versões?"
        // Nome explícito: o gerado passaria de 63 caracteres (limite do Postgres) e seria truncado.
        builder.HasIndex(a => new { a.TenantId, a.UsuarioId, a.VersaoTermosDeUso, a.VersaoPoliticaPrivacidade })
            .HasDatabaseName("IX_aceites_termos_usuario_versoes");
    }
}
