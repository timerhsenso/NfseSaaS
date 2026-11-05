using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        // CPF (11) ou CNPJ (14) do titular da conta, sem máscara.
        builder.Property(t => t.Documento)
            .HasMaxLength(14)
            .IsRequired();

        // Contas criadas antes do cadastro em etapas são todas "Empresa".
        builder.Property(t => t.Perfil)
            .HasDefaultValue(PerfilConta.Empresa)
            .HasSentinel(default(PerfilConta));

        builder.Property(t => t.RazaoSocial)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.NomeFantasia)
            .HasMaxLength(200);

        builder.Property(t => t.MotivoBloqueio)
            .HasMaxLength(300);

        builder.HasIndex(t => t.Documento)
            .IsUnique();

        builder.Property(t => t.CodigoIndicacao)
            .HasMaxLength(12);

        builder.Property(t => t.OrigemCadastro)
            .HasMaxLength(40);

        // Único quando preenchido (o Postgres aceita vários NULL num índice único).
        builder.HasIndex(t => t.CodigoIndicacao)
            .IsUnique()
            .HasDatabaseName("IX_tenants_CodigoIndicacao");

        // Quem indicou não pode ser apagado enquanto houver conta indicada por ele.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(t => t.IndicadoPorTenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
