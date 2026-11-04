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

    /// <summary>
    /// false = conta suspensa pelo operador da plataforma (painel
    /// Plataforma): ninguém do tenant entra, as sessões abertas caem em até
    /// 1 minuto e nenhuma nota é emitida (nem pela automação).
    /// </summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Quando a conta foi suspensa (nulo se ativa).</summary>
    public DateTimeOffset? BloqueadoEm { get; set; }

    /// <summary>Por que foi suspensa (ex.: inadimplência, pedido do cliente) — só o operador vê.</summary>
    public string? MotivoBloqueio { get; set; }
}
