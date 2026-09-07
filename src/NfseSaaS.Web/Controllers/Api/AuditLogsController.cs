using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.AuditLogs;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Consulta ao AuditLog — somente leitura por design: não existe (e não
/// deve existir) POST/PUT/DELETE aqui. AuditLog só é escrito internamente
/// por IAuditLogWriter, como efeito colateral de outras operações; expor
/// escrita por API tornaria o log adulterável, o que anula o propósito
/// dele.
/// </summary>
[ApiController]
[Authorize]
[Route("api/auditlogs")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IListarAuditLogsUseCase _listarAuditLogs;
    private readonly IObterAuditLogPorIdUseCase _obterAuditLogPorId;

    public AuditLogsController(
        IListarAuditLogsUseCase listarAuditLogs,
        IObterAuditLogPorIdUseCase obterAuditLogPorId)
    {
        _listarAuditLogs = listarAuditLogs;
        _obterAuditLogPorId = obterAuditLogPorId;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? operacao = null,
        [FromQuery] string? entidade = null,
        [FromQuery] Guid? entidadeId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTimeOffset? dataInicio = null,
        [FromQuery] DateTimeOffset? dataFim = null,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarAuditLogs.ExecutarAsync(
            new ListarAuditLogsRequest(page, pageSize, operacao, entidade, entidadeId, userId, dataInicio, dataFim),
            cancellationToken);

        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var auditLog = await _obterAuditLogPorId.ExecutarAsync(id, cancellationToken);
        return Ok(auditLog);
    }
}
