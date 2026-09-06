using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Serviço cadastrado por uma Empresa, usado como base para a emissão da DPS.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Servico : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public string Descricao { get; set; } = string.Empty;

    /// <summary>Código de tributação nacional (cTribNac) do layout da NFS-e Nacional.</summary>
    public string CodigoTributacaoNacional { get; set; } = string.Empty;

    public decimal ValorPadrao { get; set; }

    public bool Ativo { get; set; } = true;
}
