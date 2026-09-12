using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

public sealed record EmailLogResponse(
    Guid Id,
    string Tipo,
    string Destinatario,
    string Assunto,
    string Status,
    string? ErroDetalhe,
    int Tentativas,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EnviadoEm);

/// <summary>
/// Consulta e reenvio dos e-mails que o sistema tenta mandar (convite,
/// reset de senha, esqueci senha, aviso de troca de senha — ver
/// TipoEmailCatalogo). O envio de verdade é feito em segundo plano por
/// EmailDispatchHostedService; esta API só lê/reenfileira o que já está
/// registrado em EmailLog.
/// </summary>
[ApiController]
[RequerPermissao(TelaCatalogo.Emails, AcaoPermissao.Consultar)]
[Route("api/emails")]
public sealed class EmailLogsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBackgroundEmailQueue _fila;

    public EmailLogsController(AppDbContext db, IBackgroundEmailQueue fila)
    {
        _db = db;
        _fila = fila;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] StatusEmailLog? status = null,
        [FromQuery] string? tipo = null,
        [FromQuery] string? busca = null,
        CancellationToken cancellationToken = default)
    {
        var (paginaNormalizada, tamanhoNormalizado) = Paginacao.Normalizar(page, pageSize);

        var query = _db.EmailLogs.AsNoTracking().AsQueryable();

        if (status is not null)
            query = query.Where(e => e.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(tipo))
            query = query.Where(e => e.Tipo == tipo);

        if (!string.IsNullOrWhiteSpace(busca))
            query = query.Where(e => EF.Functions.ILike(e.Destinatario, $"%{busca}%"));

        var totalCount = await query.CountAsync(cancellationToken);

        var itens = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((paginaNormalizada - 1) * tamanhoNormalizado)
            .Take(tamanhoNormalizado)
            .Select(e => new EmailLogResponse(
                e.Id, e.Tipo, e.Destinatario, e.Assunto, e.Status.ToString(), e.ErroDetalhe, e.Tentativas, e.CreatedAt, e.EnviadoEm))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<EmailLogResponse>
        {
            Items = itens,
            Page = paginaNormalizada,
            PageSize = tamanhoNormalizado,
            TotalCount = totalCount
        });
    }

    /// <summary>Reenvia o e-mail EXATAMENTE como foi montado na tentativa original — não regenera token nem nada, só manda de novo o mesmo corpo.</summary>
    [RequerPermissao(TelaCatalogo.Emails, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/reenviar")]
    public async Task<IActionResult> Reenviar(Guid id, CancellationToken cancellationToken)
    {
        var log = await _db.EmailLogs.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (log is null)
            return NotFound();

        log.Status = StatusEmailLog.Pendente;
        log.ErroDetalhe = null;
        await _db.SaveChangesAsync(cancellationToken);

        _fila.Enfileirar(new ItemFilaEmail(log.Id, log.Destinatario, log.Assunto, log.CorpoHtml));

        return NoContent();
    }
}
