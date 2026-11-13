using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// PL1: valor de um recurso do catálogo (CatalogoRecursos, no código) num
/// plano. Entidade GLOBAL, editada em Plataforma › Planos › Recursos. Sem
/// linha para o recurso = vale o padrão do catálogo (o comportamento de
/// antes do PL1). Ligado vale para recurso liga/desliga; Limite para
/// recurso com limite (nulo = ilimitado).
/// </summary>
public sealed class PlanoRecurso : BaseEntity
{
    public Guid PlanoId { get; set; }

    /// <summary>Código do recurso no catálogo (ex.: "logo-propria").</summary>
    public string Recurso { get; set; } = string.Empty;

    public bool? Ligado { get; set; }

    public int? Limite { get; set; }
}

/// <summary>
/// PL1: exceção de um recurso para UMA conta (cortesia, negociação, teste
/// de recurso novo). Vence o plano enquanto valer (ValidaAte nulo = sem
/// prazo). Criada pelo operador da plataforma, com motivo; fica no
/// Histórico do operador. Uma por recurso e conta.
/// </summary>
public sealed class ExcecaoRecursoConta : BaseEntity
{
    public Guid TenantId { get; set; }

    public string Recurso { get; set; } = string.Empty;

    public bool? Ligado { get; set; }

    public int? Limite { get; set; }

    public string Motivo { get; set; } = string.Empty;

    /// <summary>Último dia em que a exceção vale (inclusive). Nulo = sem prazo.</summary>
    public DateOnly? ValidaAte { get; set; }

    public Guid? CriadoPorUsuarioId { get; set; }
}
