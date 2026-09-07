namespace NfseSaaS.Application.UseCases.AuditLogs;

public sealed record AuditLogResponse(
    Guid Id,
    Guid? UserId,
    DateTimeOffset DataHora,
    string Operacao,
    string Entidade,
    Guid? EntidadeId,
    string? IpAddress,
    string? Dados);
