using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

        builder.Property(c => c.Email)
            .HasMaxLength(200);

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

        // Mesmo CPF/CNPJ não pode se repetir para a mesma Empresa — mas
        // PODE ser cliente de duas Empresas diferentes do mesmo Tenant
        // (ex.: escritório de contabilidade que atende o mesmo cliente
        // final através de mais de uma empresa que gerencia).
        builder.HasIndex(c => new { c.TenantId, c.EmpresaId, c.CpfCnpj })
            .IsUnique();

        // FK real (sem navigation property — este projeto usa acesso
        // direto por EmpresaId, não navegação de grafo de objetos).
        // Restrict = ON DELETE RESTRICT: o Postgres recusa excluir a
        // Empresa se ainda existir Cliente apontando pra ela. É o
        // backstop de banco para a mesma regra já checada em
        // ExcluirEmpresaUseCase — fecha a janela de corrida entre o
        // COUNT() e o DELETE.
        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(c => c.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
