using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ConfiguracaoAutomacaoNotaMensalConfiguration : IEntityTypeConfiguration<ConfiguracaoAutomacaoNotaMensal>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoAutomacaoNotaMensal> builder)
    {
        builder.ToTable("configuracoes_automacao_nota_mensal");

        builder.HasKey(c => c.Id);

        // 1:1 com Empresa — só uma configuração por Empresa.
        builder.HasIndex(c => c.EmpresaId).IsUnique();

        builder.HasOne<Empresa>()
            .WithOne()
            .HasForeignKey<ConfiguracaoAutomacaoNotaMensal>(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.Frequencia).HasConversion<int>();
        builder.Property(c => c.Modo).HasConversion<int>();
        builder.Property(c => c.DiaSemana).HasConversion<int?>();
    }
}
