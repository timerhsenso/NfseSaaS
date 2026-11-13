using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CpfCnpj)
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(c => c.Nome)
            .HasMaxLength(200)
            .IsRequired();

        // HasDefaultValue: a migration preenche os Clientes existentes com
        // "não retém" (mesma cautela de todo campo novo neste projeto).
        builder.Property(c => c.RetemIss)
            .HasDefaultValue(false);

        // C6: percentuais de retenção federal (nulo = não retém).
        builder.Property(c => c.RetencaoIrrfPercentual).HasPrecision(5, 2);
        builder.Property(c => c.RetencaoPisPercentual).HasPrecision(5, 2);
        builder.Property(c => c.RetencaoCofinsPercentual).HasPrecision(5, 2);
        builder.Property(c => c.RetencaoCsllPercentual).HasPrecision(5, 2);
        builder.Property(c => c.RetencaoInssPercentual).HasPrecision(5, 2);

        builder.Property(c => c.Apelido)
            .HasMaxLength(Cliente.TamanhoMaximoApelido)
            .IsRequired();

        builder.Property(c => c.Email)
            .HasMaxLength(200);

        builder.Property(c => c.EmailsCopia)
            .HasMaxLength(EmailsCopiaCliente.TamanhoMaximoColuna);

        builder.Property(c => c.Telefone)
            .HasMaxLength(20);

        builder.Property(c => c.CodigoMunicipio)
            .HasMaxLength(7);

        builder.Property(c => c.Cep)
            .HasMaxLength(8);

        builder.Property(c => c.Logradouro)
            .HasMaxLength(200);

        builder.Property(c => c.Numero)
            .HasMaxLength(20);

        builder.Property(c => c.Complemento)
            .HasMaxLength(100);

        builder.Property(c => c.Bairro)
            .HasMaxLength(100);

        builder.Property(c => c.Uf)
            .HasMaxLength(2);

        // C10: EmpresaId nulo = cliente compartilhado entre as empresas da
        // conta. Coluna calculada, não vai ao banco.
        builder.Ignore(c => c.Compartilhado);

        // Mesmo CPF/CNPJ não se repete no mesmo escopo: um compartilhado por
        // conta e um exclusivo por Empresa. Os dois PODEM coexistir (na
        // Empresa que tem o exclusivo, ele vence o compartilhado) — por isso
        // dois índices parciais, um para cada escopo (no Postgres, NULL nunca
        // colide num índice único comum).
        builder.HasIndex(c => new { c.TenantId, c.EmpresaId, c.CpfCnpj })
            .IsUnique()
            .HasFilter("\"EmpresaId\" IS NOT NULL");

        builder.HasIndex(c => new { c.TenantId, c.CpfCnpj })
            .IsUnique()
            .HasFilter("\"EmpresaId\" IS NULL");

        // Apelido: mesmo desenho. O índice compara exato (case-sensitive no
        // Postgres) e só dentro do escopo — é o backstop de banco; a regra de
        // verdade (sem diferenciar maiúsculas/minúsculas e contando a lista
        // de cada Empresa) é a de ApelidoCliente.
        builder.HasIndex(c => new { c.TenantId, c.EmpresaId, c.Apelido })
            .IsUnique()
            .HasFilter("\"EmpresaId\" IS NOT NULL");

        builder.HasIndex(c => new { c.TenantId, c.Apelido })
            .IsUnique()
            .HasFilter("\"EmpresaId\" IS NULL");

        // FK real (sem navigation property — este projeto usa acesso
        // direto por EmpresaId, não navegação de grafo de objetos).
        // Restrict = ON DELETE RESTRICT: o Postgres recusa excluir a
        // Empresa se ainda existir Cliente apontando pra ela. É o
        // backstop de banco para a mesma regra já checada em
        // ExcluirEmpresaUseCase — fecha a janela de corrida entre o
        // COUNT() e o DELETE. Cliente compartilhado (EmpresaId nulo) não
        // prende nenhuma Empresa.
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Modelo do e-mail da nota (cadastro Modelos de e-mail). Excluir o
        // modelo devolve quem o usava ao padrão da conta.
        builder.HasOne<ModeloEmailNota>()
            .WithMany()
            .HasForeignKey(c => c.ModeloEmailNotaId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
