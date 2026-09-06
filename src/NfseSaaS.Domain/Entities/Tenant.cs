using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Representa um cliente do SaaS (a assinatura/conta). Entidade GLOBAL:
/// NÃO implementa ITenantEntity e NÃO sofre o Global Query Filter de tenant
/// — do contrário seria impossível localizar o próprio tenant antes de
/// existir um TenantId "atual" resolvido.
/// </summary>
public sealed class Tenant : BaseEntity
{
    public string Cnpj { get; set; } = string.Empty;

    public string RazaoSocial { get; set; } = string.Empty;

    public string? NomeFantasia { get; set; }

    public bool Ativo { get; set; } = true;
}
