namespace NfseSaaS.Application.UseCases.AuditLogs;

public interface IObterAuditLogPorIdUseCase
{
    Task<AuditLogResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
