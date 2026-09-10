using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ContratoServicoConfiguration : IEntityTypeConfiguration<ContratoServico>
{
    public void Configure(EntityTypeBuilder<ContratoServico> builder)
    {
        builder.ToTable("contrato_servicos");

        builder.HasKey(cs => cs.Id);

        builder.Property(cs => cs.Quantidade)
            .HasColumnType("numeric(18,4)");

        builder.Property(cs => cs.ValorUnitario)
            .HasColumnType("numeric(18,2)");

        // ValorTotal é sempre Quantidade * ValorUnitario, nunca
        // persistido — mesmo raciocínio de SituacaoContrato (calculado)
        // usado em outras partes do projeto.
        builder.Ignore(cs => cs.ValorTotal);

        builder.HasIndex(cs => new { cs.TenantId, cs.ContratoId });

        // Linha de detalhe do Contrato: some junto quando o Contrato é
        // excluído (Contrato só é excluível se não tiver Nfse/Reajuste
        // vinculado — ver ExcluirContratoUseCase — então não há histórico
        // fiscal em risco aqui).
        builder.HasOne<Contrato>()
            .WithMany()
            .HasForeignKey(cs => cs.ContratoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Servico não pode ser excluído enquanto referenciado por alguma
        // linha (ver ExcluirServicoUseCase, atualizado na Fase 6 pra
        // checar aqui em vez de Contrato.ServicoId).
        builder.HasOne<Servico>()
            .WithMany()
            .HasForeignKey(cs => cs.ServicoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
