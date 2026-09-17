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

    /// <summary>
    /// Pra processos que rodam FORA de uma requisição HTTP (jobs em
    /// background, ver AutomacaoNotaMensalJob) — não existe Claim pra
    /// ler nesses casos, então quem dispara o processo já sabe de qual
    /// Tenant se trata (geralmente resolvido via EmpresaId, com
    /// .IgnoreQueryFilters()) e define aqui, explicitamente, antes de
    /// chamar qualquer UseCase que dependa do Global Query Filter.
    /// Mesma ideia e mesmo nome do AppDbContext.TenantIdOverrideDeSistema,
    /// pelo mesmo motivo — os dois precisam ser setados juntos (ver
    /// AutomacaoNotaMensalJob).
    /// </summary>
    void DefinirTenantIdDeSistema(Guid tenantId);
}
