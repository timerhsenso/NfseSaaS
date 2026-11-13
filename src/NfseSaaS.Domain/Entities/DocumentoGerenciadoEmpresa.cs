using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// DOC4: um tipo do catálogo que a Empresa escolheu gerenciar (aparece em
/// Documentos da empresa). Enquanto a Empresa não escolhe
/// (Empresa.DocumentosEscolhidosEm nulo), valem os tipos padrão do catálogo
/// e os que ela já tem arquivo, sem linha aqui.
///
/// LinkProprio: link oficial que a empresa cadastrou para esse documento
/// (vence os do catálogo). Também alimenta a sugestão para outras empresas
/// do mesmo município/UF (só o endereço; nunca o arquivo nem a empresa).
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class DocumentoGerenciadoEmpresa : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public int TipoDocumentoId { get; set; }

    public string? LinkProprio { get; set; }
}
