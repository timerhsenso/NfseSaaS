using NfseSaaS.Application.Common;

namespace NfseSaaS.Application.UseCases.AuditLogs;

public interface IListarAuditLogsUseCase
{
    Task<PagedResult<AuditLogResponse>> ExecutarAsync(ListarAuditLogsRequest request, CancellationToken cancellationToken);
}
