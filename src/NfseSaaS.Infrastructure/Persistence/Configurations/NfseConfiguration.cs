using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class NfseConfiguration : IEntityTypeConfiguration<Nfse>
{
    public void Configure(EntityTypeBuilder<Nfse> builder)
    {
        builder.ToTable("notas_fiscais");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.SerieDps)
            .HasMaxLength(5)
            .IsRequired();

        builder.Property(n => n.NumeroNfse)
            .HasMaxLength(80);

        builder.Property(n => n.ChaveAcesso)
            .HasMaxLength(60);

        builder.Property(n => n.ValorServico)
            .HasColumnType("numeric(18,2)");

        builder.Property(n => n.DescricaoServico)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(n => n.CodigoTributacaoNacional)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(n => n.CodigoNbs)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(n => n.TribIssqn)
            .HasMaxLength(1)
            .IsRequired();

        builder.Property(n => n.TpRetIssqn)
            .HasMaxLength(1)
            .IsRequired();

        builder.Property(n => n.CstPisCofins)
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(n => n.TpRetPisCofins)
            .HasMaxLength(1)
            .IsRequired();

        builder.Property(n => n.PercentualTotalTributosSimplesNacional)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(100);

        builder.Property(n => n.ValorLiquido)
            .HasColumnType("numeric(18,2)");

        builder.Property(n => n.SnapshotFiscalJson)
            .HasColumnType("jsonb");

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(n => n.CodigoErro)
            .HasMaxLength(20);

        builder.Property(n => n.MensagemErro)
            .HasMaxLength(1000);

        // Não pode existir duas DPS com o mesmo número+série para a mesma empresa/tenant.
        builder.HasIndex(n => new { n.TenantId, n.EmpresaId, n.NumeroDps, n.SerieDps })
            .IsUnique();

        builder.HasIndex(n => n.ChaveAcesso)
            .IsUnique()
            .HasFilter("\"ChaveAcesso\" IS NOT NULL");

        // Duas emissões com a mesma IdempotencyKey, para a mesma
        // Empresa/Tenant, são a MESMA tentativa lógica (retry) — nunca
        // duas notas distintas. Filtrado porque a maioria das emissões
        // não informa chave (comportamento antigo continua permitido).
        builder.HasIndex(n => new { n.TenantId, n.EmpresaId, n.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        // FKs reais — ver comentário completo em ClienteConfiguration.
        // Restrict nos dois: uma Nfse (documento fiscal, mesmo Rejeitada)
        // nunca pode ficar órfã de Empresa ou Cliente.
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(n => n.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(n => n.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Contrato>()
            .WithMany()
            .HasForeignKey(n => n.ContratoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
