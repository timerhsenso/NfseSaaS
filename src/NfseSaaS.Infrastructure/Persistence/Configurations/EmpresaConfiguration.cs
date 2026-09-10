using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

public sealed class EmpresaConfiguration : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> builder)
    {
        builder.ToTable("empresas");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Cnpj)
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(e => e.RazaoSocial)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.InscricaoMunicipal)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.CodigoMunicipio)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(e => e.Telefone)
            .HasMaxLength(20);

        builder.Property(e => e.Email)
            .HasMaxLength(200);

        builder.Property(e => e.Cep)
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(e => e.Logradouro)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Numero)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Complemento)
            .HasMaxLength(100);

        builder.Property(e => e.Bairro)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Uf)
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(e => e.OpSimpNac)
            .HasMaxLength(1);

        builder.Property(e => e.RegApTribSN)
            .HasMaxLength(1);

        builder.Property(e => e.RegEspTrib)
            .HasMaxLength(1);

        builder.Property(e => e.TribIssqn)
            .HasMaxLength(1);

        builder.Property(e => e.TpRetIssqn)
            .HasMaxLength(1);

        builder.Property(e => e.CstPisCofins)
            .HasMaxLength(2);

        builder.Property(e => e.TpRetPisCofins)
            .HasMaxLength(1);

        builder.Property(e => e.PercentualTotalTributosSimplesNacional)
            .HasMaxLength(10);

        // Grava como int (mesmo valor do tpAmb — ver TipoAmbiente). Default
        // explícito no banco (não só no C#) por segurança: qualquer INSERT
        // que por algum motivo não passe pelo EF Core (script manual,
        // outra ferramenta) ainda cai em Homologacao, nunca em Producao.
        builder.Property(e => e.TipoAmbiente)
            .HasConversion<int>()
            .HasDefaultValue(TipoAmbiente.Homologacao);

        // Um mesmo CNPJ não pode se repetir dentro do mesmo tenant.
        builder.HasIndex(e => new { e.TenantId, e.Cnpj })
            .IsUnique();
    }
}
