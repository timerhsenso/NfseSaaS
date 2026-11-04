using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class MovimentacaoValorContratoConfiguration : IEntityTypeConfiguration<MovimentacaoValorContrato>
{
    public void Configure(EntityTypeBuilder<MovimentacaoValorContrato> builder)
    {
        builder.ToTable("movimentacoes_valor_contrato");

        builder.HasKey(m => m.Id);

        // HasDefaultValue nos enums novos (mesma cautela de todo enum novo
        // neste projeto): as linhas migradas da antiga reajustes_contrato
        // recebem Reajuste/Aplicado. Os enums começam em 1, então o default
        // do CLR (0) nunca coincide com um valor válido — o EF sempre envia
        // o valor de verdade no INSERT.
        // Sentinel 0 explícito (os enums começam em 1): mesmo comportamento
        // descrito acima, sem o aviso de "sentinel" do EF na subida.
        builder.Property(m => m.Tipo)
            .HasConversion<int>()
            .HasDefaultValue(TipoMovimentacaoValor.Reajuste)
            .HasSentinel(default(TipoMovimentacaoValor));

        builder.Property(m => m.Status)
            .HasConversion<int>()
            .HasDefaultValue(StatusMovimentacaoValor.Aplicado)
            .HasSentinel(default(StatusMovimentacaoValor));

        builder.Property(m => m.ValorAnterior).HasColumnType("numeric(18,2)");
        builder.Property(m => m.ValorNovo).HasColumnType("numeric(18,2)");
        builder.Property(m => m.PercentualCalculado).HasColumnType("numeric(9,4)");
        builder.Property(m => m.PercentualAplicado).HasColumnType("numeric(9,4)");
        builder.Property(m => m.Indice).HasConversion<int?>();
        builder.Property(m => m.Motivo).HasMaxLength(MovimentacaoValorContrato.TamanhoMaximoMotivo);
        builder.Property(m => m.ComposicaoJson).HasColumnType("jsonb");
        builder.Property(m => m.Automatico).HasDefaultValue(false);

        builder.HasIndex(m => new { m.TenantId, m.ContratoId, m.DataVigencia });

        // FK real, Restrict: histórico é append-only. Excluir um Contrato só
        // é permitido sem eventos além do Valor inicial (ver
        // ExcluirContratoUseCase, que apaga esse evento antes).
        builder.HasOne<Contrato>()
            .WithMany()
            .HasForeignKey(m => m.ContratoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
