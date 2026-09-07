using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.AuditLogs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Só leitura — não há Cadastrar/Atualizar/Desativar para AuditLog, ele é
/// escrito exclusivamente por IAuditLogWriter (ver Infrastructure.Auditing)
/// como efeito colateral de outras operações, nunca diretamente pela API.
/// O isolamento por Tenant já vem de graça do Global Query Filter do
/// AppDbContext (AuditLog é ITenantEntity) — nenhum filtro manual aqui.
/// </summary>
public sealed class ListarAuditLogsUseCase : IListarAuditLogsUseCase
{
    private readonly AppDbContext _db;

    public ListarAuditLogsUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AuditLogResponse>> ExecutarAsync(ListarAuditLogsRequest request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paginacao.Normalizar(request.Page, request.PageSize);

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Operacao))
            query = query.Where(a => a.Operacao == request.Operacao);

        if (!string.IsNullOrWhiteSpace(request.Entidade))
            query = query.Where(a => a.Entidade == request.Entidade);

        if (request.EntidadeId is not null)
            query = query.Where(a => a.EntidadeId == request.EntidadeId.Value);

        if (request.UserId is not null)
            query = query.Where(a => a.UserId == request.UserId.Value);

        if (request.DataInicio is not null)
            query = query.Where(a => a.DataHora >= request.DataInicio.Value);

        if (request.DataFim is not null)
            query = query.Where(a => a.DataHora <= request.DataFim.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var itens = await query
            .OrderByDescending(a => a.DataHora)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogResponse(
                a.Id, a.UserId, a.DataHora, a.Operacao, a.Entidade, a.EntidadeId, a.IpAddress, a.Dados))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogResponse>
        {
            Items = itens,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
