using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class GrupoTelaConfiguration : IEntityTypeConfiguration<GrupoTela>
{
    public void Configure(EntityTypeBuilder<GrupoTela> builder)
    {
        builder.ToTable("grupo_telas");

        builder.HasKey(gt => gt.Id);

        builder.HasIndex(gt => new { gt.TenantId, gt.GrupoId, gt.TelaId })
            .IsUnique();

        // Some junto quando o Grupo é excluído — é literalmente a matriz
        // de permissão DAQUELE grupo, não faz sentido órfã.
        builder.HasOne<Grupo>()
            .WithMany()
            .HasForeignKey(gt => gt.GrupoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tela não tem endpoint de exclusão (catálogo fixo do sistema,
        // seedado no startup) — Restrict aqui é só defesa, nunca deve
        // disparar na prática.
        builder.HasOne<Tela>()
            .WithMany()
            .HasForeignKey(gt => gt.TelaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
