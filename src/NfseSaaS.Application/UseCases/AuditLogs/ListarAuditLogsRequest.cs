namespace NfseSaaS.Application.UseCases.AuditLogs;

public sealed record ListarAuditLogsRequest(
    int Page = 1,
    int PageSize = 20,
    string? Operacao = null,
    string? Entidade = null,
    Guid? EntidadeId = null,
    Guid? UserId = null,
    DateTimeOffset? DataInicio = null,
    DateTimeOffset? DataFim = null);
