using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

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

        // HasDefaultValue nos dois — mesmo cuidado que faltou em
        // Nfse.TipoAmbiente (bug real: sem default, linhas antigas
        // migradas ficam com 0/CLR-default em vez do valor esperado).
        builder.Property(c => c.Status)
            .HasConversion<int>()
            .HasDefaultValue(StatusContrato.Ativo);

        builder.Property(c => c.TipoCobranca)
            .HasConversion<int>()
            .HasDefaultValue(TipoCobrancaContrato.Avulso);

        builder.HasIndex(c => new { c.TenantId, c.EmpresaId });
        builder.HasIndex(c => new { c.TenantId, c.ClienteId });

        // FKs reais (sem navigation property — mesmo padrão de
        // Cliente/Servico/Nfse neste projeto: acesso direto por Id, não
        // navegação de grafo de objetos). Restrict nas 2: um Contrato
        // sempre precisa de uma Empresa e um Cliente válidos — excluir
        // qualquer um deles enquanto o Contrato existir é bloqueado pelo
        // banco (backstop da mesma checagem já feita nos use cases de
        // exclusão correspondentes). FK pra Servico saiu daqui na Fase 6
        // — agora mora em ContratoServico (1 Contrato pode ter N
        // serviços).
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(c => c.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
