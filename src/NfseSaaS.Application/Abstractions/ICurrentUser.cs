namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Abstração que expõe o usuário autenticado da requisição atual — usado
/// por IAuditLogWriter para preencher AuditLog.UserId/IpAddress. Mesmo
/// princípio do ICurrentTenant: resolve EXCLUSIVAMENTE a partir dos Claims
/// da requisição autenticada, nunca de um valor vindo solto do cliente.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? IpAddress { get; }
}
