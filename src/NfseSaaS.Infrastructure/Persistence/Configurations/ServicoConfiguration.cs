using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ServicoConfiguration : IEntityTypeConfiguration<Servico>
{
    public void Configure(EntityTypeBuilder<Servico> builder)
    {
        builder.ToTable("servicos");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Descricao)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(s => s.CodigoTributacaoNacional)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(s => s.CodigoNbs)
            .HasMaxLength(10);

        builder.Property(s => s.ValorPadrao)
            .HasColumnType("numeric(18,2)");

        builder.HasIndex(s => new { s.TenantId, s.EmpresaId });

        // FK real — ver comentário completo em ClienteConfiguration.
        // Note que isso protege Empresa→Servico (não deixa excluir a
        // Empresa com Servico vinculado); NÃO existe (e não deve existir)
        // uma FK de Nfse para Servico, ver ExcluirServicoUseCase.
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(s => s.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
