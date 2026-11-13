using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// DOC1: um arquivo (PDF) de documento da Empresa — certidão, cartão CNPJ,
/// contrato social. Cada envio é uma VERSÃO nova: a versão que vale é a mais
/// recente do mesmo "lugar" (Empresa + Tipo + NomeChave); as anteriores
/// ficam no histórico. O arquivo fica em disco
/// ({BasePath}/{TenantId}/{EmpresaId}/documentos/{Id}.pdf, ver
/// DocumentoEmpresaFileStore); aqui só os dados.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class DocumentoEmpresa : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    /// <summary>
    /// Tipo do catálogo (TipoDocumento.Id; coluna "Tipo", que já existia).
    /// 99 = "Outro documento", com o nome em NomePersonalizado.
    /// </summary>
    public int Tipo { get; set; }

    /// <summary>Nome dado pelo usuário quando Tipo = Outro (ex.: "Alvará de funcionamento"); nulo nos demais.</summary>
    public string? NomePersonalizado { get; set; }

    /// <summary>
    /// Chave do "lugar" do documento: vazia nos tipos fixos; nos "Outro", o
    /// nome em minúsculas e sem espaços sobrando — "Alvará" e "alvará "
    /// caem no mesmo histórico.
    /// </summary>
    public string NomeChave { get; set; } = string.Empty;

    public DateOnly? DataEmissao { get; set; }

    /// <summary>Último dia em que o documento vale (inclusive). Nulo = sem validade (contrato social, cartão CNPJ).</summary>
    public DateOnly? ValidaAte { get; set; }

    /// <summary>Nome do arquivo enviado, só para exibir e baixar (o nome em disco é o Id).</summary>
    public string NomeArquivo { get; set; } = string.Empty;

    public long TamanhoBytes { get; set; }

    public string? Observacao { get; set; }

    public Guid? EnviadoPorUsuarioId { get; set; }
}
