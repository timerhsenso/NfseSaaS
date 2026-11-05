using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class PlanoConfiguration : IEntityTypeConfiguration<Plano>
{
    public void Configure(EntityTypeBuilder<Plano> builder)
    {
        builder.ToTable("planos");
        builder.HasKey(p => p.Id);

        // A Assinatura guarda o código (assinaturas.Plano), não o Id.
        builder.HasIndex(p => p.Codigo).IsUnique();

        builder.Property(p => p.Nome).IsRequired().HasMaxLength(60);
        builder.Property(p => p.Descricao).IsRequired().HasMaxLength(200);
        // Enum como inteiro, sem valor padrão no banco (o valor vem sempre do código).
        builder.Property(p => p.Publico).IsRequired();
    }
}

public sealed class PrecoPlanoConfiguration : IEntityTypeConfiguration<PrecoPlano>
{
    public void Configure(EntityTypeBuilder<PrecoPlano> builder)
    {
        builder.ToTable("precos_planos");
        builder.HasKey(p => p.Id);

        builder.HasOne<Plano>()
            .WithMany()
            .HasForeignKey(p => p.PlanoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.PrecoMensal).HasColumnType("numeric(10,2)");
        builder.Property(p => p.PrecoFundador).HasColumnType("numeric(10,2)");
        builder.Property(p => p.Motivo).HasMaxLength(200);

        // Um preço por plano e data (cadastrar de novo na mesma data corrige o existente).
        builder.HasIndex(p => new { p.PlanoId, p.VigenteDesde }).IsUnique();
    }
}

public sealed class VagasFundadorConfiguration : IEntityTypeConfiguration<VagasFundador>
{
    public void Configure(EntityTypeBuilder<VagasFundador> builder)
    {
        builder.ToTable("vagas_fundador");
        builder.HasKey(v => v.Publico);
        builder.Property(v => v.Publico).ValueGeneratedNever();
    }
}
