using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence.Seeders;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

/// <summary>DOC4: catálogo geral de tipos de documento (global).</summary>
public sealed class TipoDocumentoConfiguration : IEntityTypeConfiguration<TipoDocumento>
{
    public void Configure(EntityTypeBuilder<TipoDocumento> builder)
    {
        builder.ToTable("tipos_documento");

        builder.HasKey(t => t.Id);
        // Ids 1 a 99 são os fixos (HasData); os criados pelo seeder e pelo operador começam em 1000.
        builder.Property(t => t.Id)
            .UseIdentityByDefaultColumn()
            .HasIdentityOptions(startValue: TiposDocumentoSistema.PrimeiroIdLivre);

        builder.Property(t => t.Codigo).HasMaxLength(40);
        builder.Property(t => t.Nome).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Orgao).HasMaxLength(100);
        builder.Property(t => t.LinkEmissao).HasMaxLength(500);
        builder.Property(t => t.Categoria).HasDefaultValue(Domain.Enums.CategoriaDocumento.Outros);
        builder.Property(t => t.Abrangencia).HasDefaultValue(Domain.Enums.AbrangenciaDocumento.Nacional);
        builder.Property(t => t.Ativo).HasDefaultValue(true);

        builder.HasIndex(t => t.Codigo).IsUnique();

        builder.HasData(CatalogoDocumentosSeeder.Sistema());
    }
}

/// <summary>DOC4: link oficial do operador por UF ou município.</summary>
public sealed class LinkLocalDocumentoConfiguration : IEntityTypeConfiguration<LinkLocalDocumento>
{
    public void Configure(EntityTypeBuilder<LinkLocalDocumento> builder)
    {
        builder.ToTable("links_locais_documento");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Uf).HasMaxLength(2);
        builder.Property(l => l.CodigoMunicipio).HasMaxLength(7);
        builder.Property(l => l.Link).HasMaxLength(500).IsRequired();

        // Um link por tipo e lugar (nulo conta como igual: (tipo, "BA", nulo) só uma vez).
        builder.HasIndex(l => new { l.TipoDocumentoId, l.Uf, l.CodigoMunicipio })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasOne<TipoDocumento>()
            .WithMany()
            .HasForeignKey(l => l.TipoDocumentoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>DOC4: pacotes por ramo (global).</summary>
public sealed class PacoteDocumentoConfiguration : IEntityTypeConfiguration<PacoteDocumento>
{
    public void Configure(EntityTypeBuilder<PacoteDocumento> builder)
    {
        builder.ToTable("pacotes_documento");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Codigo).HasMaxLength(40);
        builder.Property(p => p.Nome).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Descricao).HasMaxLength(200);
        builder.Property(p => p.TiposDocumentoIds).HasColumnType("integer[]");
        builder.Property(p => p.Ativo).HasDefaultValue(true);

        builder.HasIndex(p => p.Codigo).IsUnique();
    }
}

/// <summary>DOC4: documentos que cada Empresa gerencia (e o link próprio dela).</summary>
public sealed class DocumentoGerenciadoEmpresaConfiguration : IEntityTypeConfiguration<DocumentoGerenciadoEmpresa>
{
    public void Configure(EntityTypeBuilder<DocumentoGerenciadoEmpresa> builder)
    {
        builder.ToTable("documentos_gerenciados_empresa");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.LinkProprio).HasMaxLength(500);

        builder.HasIndex(d => new { d.EmpresaId, d.TipoDocumentoId }).IsUnique();
        // Sugestão de link: empresas de um lugar que cadastraram link para o tipo.
        builder.HasIndex(d => d.TipoDocumentoId);

        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(d => d.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TipoDocumento>()
            .WithMany()
            .HasForeignKey(d => d.TipoDocumentoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
