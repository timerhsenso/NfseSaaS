using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

/// <summary>PL1: valor de cada recurso do catálogo num plano (global).</summary>
public sealed class PlanoRecursoConfiguration : IEntityTypeConfiguration<PlanoRecurso>
{
    public void Configure(EntityTypeBuilder<PlanoRecurso> builder)
    {
        builder.ToTable("planos_recursos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Recurso).IsRequired().HasMaxLength(60);
        builder.HasIndex(p => new { p.PlanoId, p.Recurso }).IsUnique();
        builder.HasOne<Plano>().WithMany().HasForeignKey(p => p.PlanoId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>PL1: exceção de recurso por conta. Global (o operador cria para qualquer conta); sempre lida por TenantId.</summary>
public sealed class ExcecaoRecursoContaConfiguration : IEntityTypeConfiguration<ExcecaoRecursoConta>
{
    public void Configure(EntityTypeBuilder<ExcecaoRecursoConta> builder)
    {
        builder.ToTable("excecoes_recurso_conta");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Recurso).IsRequired().HasMaxLength(60);
        builder.Property(e => e.Motivo).IsRequired().HasMaxLength(300);
        builder.HasIndex(e => new { e.TenantId, e.Recurso }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
