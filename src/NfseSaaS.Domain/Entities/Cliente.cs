using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Tomador de serviço (cliente) de uma Empresa dentro do Tenant.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Cliente : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    /// <summary>CPF ou CNPJ do tomador, apenas dígitos.</summary>
    public string CpfCnpj { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Telefone { get; set; }

    public bool Ativo { get; set; } = true;
}
