namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Abstração que expõe o Tenant da requisição atual. A implementação
/// concreta (em NfseSaaS.Infrastructure) resolve o TenantId a partir dos
/// Claims do usuário autenticado — NUNCA a partir de um valor recebido
/// diretamente do navegador (query string, header, body) sem validação
/// contra a sessão autenticada.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>
    /// TenantId da requisição atual, ou null quando não há tenant resolvido
    /// (ex.: endpoints públicos, health check, login).
    /// </summary>
    Guid? TenantId { get; }

    bool IsResolved { get; }
}
