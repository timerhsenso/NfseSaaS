using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Seguranca;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// SEG1 — quem precisa ligar a verificação em duas etapas (ver
/// <see cref="RegrasDuploFator.Avaliar"/>). Com ela ligada é uma consulta só;
/// desligada, confere operador, grupo administrador, a exigência da conta e
/// o primeiro acesso (primeiro aceite dos Termos). Lê sem filtro de conta e
/// confere a conta à mão: roda no filtro global, também para o operador.
/// </summary>
public sealed class ExigenciaDuploFatorService : IExigenciaDuploFatorService
{
    private readonly AppDbContext _db;
    private readonly PlataformaOptions _plataforma;

    public ExigenciaDuploFatorService(AppDbContext db, IOptions<PlataformaOptions> plataforma)
    {
        _db = db;
        _plataforma = plataforma.Value;
    }

    public async Task<ExigenciaDuploFator> AvaliarAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var usuario = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.TenantId, u.GrupoId, u.TwoFactorEnabled, u.ConviteAceitoEm })
            .FirstOrDefaultAsync(cancellationToken);
        if (usuario is null)
            return ExigenciaDuploFator.NaoExigida;

        var operador = _plataforma.UsuariosOperadoresIds.Contains(usuarioId);
        var administrador = usuario.GrupoId is { } grupoId && await _db.Grupos.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(g => g.Id == grupoId && g.TenantId == usuario.TenantId && g.EhAdministrador, cancellationToken);
        var contaExigeDesde = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == usuario.TenantId)
            .Select(t => t.DuploFatorObrigatorioDesde)
            .FirstOrDefaultAsync(cancellationToken);

        var hoje = DataBrasilia.Hoje();
        var primeiroAcesso = hoje;
        if (!usuario.TwoFactorEnabled && (administrador || contaExigeDesde is not null))
        {
            var primeiroAceite = await _db.AceitesTermos.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.UsuarioId == usuarioId && a.TenantId == usuario.TenantId)
                .OrderBy(a => a.AceitoEm)
                .Select(a => (DateTimeOffset?)a.AceitoEm)
                .FirstOrDefaultAsync(cancellationToken);
            if ((primeiroAceite ?? usuario.ConviteAceitoEm) is { } entrada)
                primeiroAcesso = DiaBrasilia(entrada);
        }

        return RegrasDuploFator.Avaliar(
            usuario.TwoFactorEnabled, operador, administrador,
            contaExigeDesde is { } desde ? DiaBrasilia(desde) : null,
            primeiroAcesso, hoje);
    }

    internal static DateOnly DiaBrasilia(DateTimeOffset momento) => DateOnly.FromDateTime(momento.UtcDateTime.AddHours(-3));
}

/// <summary>
/// Configurações da conta › Segurança: o administrador exige a verificação
/// em duas etapas de todos os usuários e vê quem ainda não ligou. Exigir
/// pede que ele mesmo já tenha ligado (senão ele próprio ficaria sem
/// acesso ao fim do prazo sem perceber).
/// </summary>
public sealed class SegurancaContaService : ISegurancaContaUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;

    public SegurancaContaService(AppDbContext db, ICurrentTenant currentTenant, ICurrentUser currentUser, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<SegurancaContaResponse> ObterAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = await ExigirAdministradorAsync(cancellationToken);
        return await MontarAsync(tenantId, usuarioId, cancellationToken);
    }

    public async Task<SegurancaContaResponse> AlterarAsync(AlterarSegurancaContaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = await ExigirAdministradorAsync(cancellationToken);
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == tenantId, cancellationToken);

        var exigiaAntes = tenant.DuploFatorObrigatorioDesde is not null;
        if (request.ExigirDeTodos == exigiaAntes)
            return await MontarAsync(tenantId, usuarioId, cancellationToken);

        if (request.ExigirDeTodos)
        {
            var ligou = await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(u => u.Id == usuarioId && u.TenantId == tenantId && u.TwoFactorEnabled, cancellationToken);
            if (!ligou)
                throw new RegraNegocioException(RegrasDuploFator.MensagemExigirSemTerLigado);
        }

        tenant.DuploFatorObrigatorioDesde = request.ExigirDeTodos ? DateTimeOffset.UtcNow : null;
        _auditLogWriter.Registrar("AlterarExigenciaDuploFator", "Tenant", tenantId, new { ExigirDeTodos = request.ExigirDeTodos });
        await _db.SaveChangesAsync(cancellationToken);

        return await MontarAsync(tenantId, usuarioId, cancellationToken);
    }

    private async Task<SegurancaContaResponse> MontarAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var desde = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.DuploFatorObrigatorioDesde)
            .FirstOrDefaultAsync(cancellationToken);

        var usuarios = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new { u.Id, u.Email, u.TwoFactorEnabled })
            .ToListAsync(cancellationToken);

        return new SegurancaContaResponse(
            desde is not null,
            desde,
            usuarios.Any(u => u.Id == usuarioId && u.TwoFactorEnabled),
            usuarios.Count,
            usuarios.Count(u => u.TwoFactorEnabled),
            usuarios.Where(u => !u.TwoFactorEnabled).Select(u => u.Email ?? string.Empty).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList(),
            RegrasDuploFator.DiasPrazoAdesao);
    }

    private async Task<(Guid TenantId, Guid UsuarioId)> ExigirAdministradorAsync(CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId || _currentUser.UserId is not { } usuarioId)
            throw new InvalidOperationException("Conta ou usuário não identificado.");

        var grupoId = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
            .Select(u => u.GrupoId)
            .FirstOrDefaultAsync(cancellationToken);
        var administrador = grupoId is { } id && await _db.Grupos.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(g => g.Id == id && g.TenantId == tenantId && g.EhAdministrador, cancellationToken);
        if (!administrador)
            throw new RegraNegocioException("Só o administrador da conta altera a segurança da conta.");

        return (tenantId, usuarioId);
    }
}
