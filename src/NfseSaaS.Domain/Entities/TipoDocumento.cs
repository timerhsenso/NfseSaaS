using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// DOC4: um tipo de documento do catálogo geral (certidões, contrato social,
/// PGR, PCMSO, alvará...). Entidade GLOBAL, mantida só pelo operador da
/// plataforma (Plataforma › Documentos). Cada empresa escolhe quais gerencia
/// (<see cref="DocumentoGerenciadoEmpresa"/>).
///
/// Nunca é apagado: o operador inativa (quem já usa continua vendo o
/// histórico). Os 8 tipos de antes do catálogo e o "Outro documento" (99)
/// mantêm os números que já estão gravados em documentos_empresa.Tipo e em
/// Cliente.DocumentosExigidos (ver TiposDocumentoSistema).
/// </summary>
public sealed class TipoDocumento
{
    public int Id { get; set; }

    /// <summary>Código estável dos tipos que o sistema semeia ("pgr", "cnd-federal"); nulo nos criados pelo operador.</summary>
    public string? Codigo { get; set; }

    public string Nome { get; set; } = string.Empty;

    public CategoriaDocumento Categoria { get; set; } = CategoriaDocumento.Outros;

    public AbrangenciaDocumento Abrangencia { get; set; } = AbrangenciaDocumento.Nacional;

    /// <summary>Quem emite (ex.: "Receita Federal", "Prefeitura").</summary>
    public string? Orgao { get; set; }

    /// <summary>Link oficial geral (vale para todo o país). Nos estaduais e municipais costuma ficar vazio.</summary>
    public string? LinkEmissao { get; set; }

    /// <summary>Validade usual em dias a partir da emissão (nulo = varia ou não vence).</summary>
    public int? ValidadeDias { get; set; }

    /// <summary>Certidões: a validade é obrigatória no envio.</summary>
    public bool ExigeValidade { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>Já vem marcado para a empresa que ainda não escolheu os documentos.</summary>
    public bool PadraoNovasEmpresas { get; set; }

    public int Ordem { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// DOC4: link oficial de um tipo de documento numa UF ou num município
/// (certidão estadual da BA, certidão municipal de Salvador). Entidade
/// GLOBAL, cadastrada pelo operador; vence a sugestão vinda das empresas.
/// Uf preenchida = vale para o estado; CodigoMunicipio preenchido = para o município.
/// </summary>
public sealed class LinkLocalDocumento : Common.BaseEntity
{
    public int TipoDocumentoId { get; set; }

    public string? Uf { get; set; }

    /// <summary>Código IBGE (7 dígitos).</summary>
    public string? CodigoMunicipio { get; set; }

    public string Link { get; set; } = string.Empty;
}

/// <summary>
/// DOC4: pacote de documentos por ramo ("Presto serviço para indústria",
/// "Vendo para órgão público"): marca vários tipos de uma vez na escolha da
/// empresa. Entidade GLOBAL, mantida pelo operador; nunca apagada (inativa).
/// </summary>
public sealed class PacoteDocumento : Common.BaseEntity
{
    /// <summary>Código estável dos pacotes que o sistema semeia; nulo nos criados pelo operador.</summary>
    public string? Codigo { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string? Descricao { get; set; }

    /// <summary>Tipos do catálogo que o pacote marca (array no Postgres).</summary>
    public List<int> TiposDocumentoIds { get; set; } = new();

    public bool Ativo { get; set; } = true;

    public int Ordem { get; set; }
}
