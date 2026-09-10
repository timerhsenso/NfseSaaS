using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ContratoConfiguration : IEntityTypeConfiguration<Contrato>
{
    public void Configure(EntityTypeBuilder<Contrato> builder)
    {
        builder.ToTable("contratos");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Descricao)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.ValorAtual)
            .HasColumnType("numeric(18,2)");

        builder.Property(c => c.IndiceReajuste)
            .HasMaxLength(30);

        builder.HasIndex(c => new { c.TenantId, c.EmpresaId });
        builder.HasIndex(c => new { c.TenantId, c.ClienteId });

        // FKs reais (sem navigation property — mesmo padrão de
        // Cliente/Servico/Nfse neste projeto: acesso direto por Id, não
        // navegação de grafo de objetos). Restrict nas 3: um Contrato
        // sempre precisa de uma Empresa, um Cliente e um Servico válidos
        // — excluir qualquer um deles enquanto o Contrato existir é
        // bloqueado pelo banco (backstop da mesma checagem já feita nos
        // use cases de exclusão correspondentes).
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(c => c.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Servico>()
            .WithMany()
            .HasForeignKey(c => c.ServicoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
