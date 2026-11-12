using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Conta;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Dados da conta (Configurações da conta): mostra o cadastro e guarda o
/// WhatsApp do dono, para o micoempresa falar com ele e, mais adiante,
/// mandar avisos (só com autorização — LGPD). Só o administrador muda.
/// </summary>
public sealed class DadosContaService : IDadosContaUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;

    public DadosContaService(AppDbContext db, ICurrentTenant currentTenant, ICurrentUser currentUser, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<DadosContaResponse> ObterAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        var conta = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        var ehAdmin = await EhAdministradorAsync(tenantId, usuarioId, cancellationToken);

        // Dono = quem entrou pelo cadastro (não por convite) — mesma regra da Plataforma.
        var responsavel = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.ConvidadoEm == null)
            .OrderBy(u => u.Email)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        var lembrete = ehAdmin && conta.WhatsApp is null
            && (conta.LembreteWhatsAppAdiadoAte is null || conta.LembreteWhatsAppAdiadoAte <= DateTimeOffset.UtcNow);

        return new DadosContaResponse(
            conta.NomeFantasia ?? conta.RazaoSocial, conta.Documento, conta.Perfil, responsavel,
            RegrasWhatsApp.Formatar(conta.WhatsApp), conta.WhatsAppAutorizadoEm is not null, ehAdmin, lembrete);
    }

    public async Task<DadosContaResponse> SalvarContatoAsync(SalvarContatoContaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        if (!await EhAdministradorAsync(tenantId, usuarioId, cancellationToken))
            throw new RegraNegocioException("Só o administrador da conta muda os dados da conta.");

        var whatsApp = RegrasWhatsApp.Validar(request.WhatsApp);
        var conta = await _db.Tenants.FirstAsync(t => t.Id == tenantId, cancellationToken);
        var antes = new { conta.WhatsApp, autorizado = conta.WhatsAppAutorizadoEm is not null };

        conta.WhatsApp = whatsApp;
        // Autorização só vale com número; mantém a data original se já autorizado.
        conta.WhatsAppAutorizadoEm = whatsApp is not null && request.AutorizaContato
            ? conta.WhatsAppAutorizadoEm ?? DateTimeOffset.UtcNow
            : null;

        _auditLogWriter.Registrar("AlterarContatoConta", "Tenant", tenantId,
            new { de = antes, para = new { whatsApp, autorizado = conta.WhatsAppAutorizadoEm is not null } });
        await _db.SaveChangesAsync(cancellationToken);

        return await ObterAsync(cancellationToken);
    }

    public async Task AdiarLembreteWhatsAppAsync(CancellationToken cancellationToken)
    {
        var (tenantId, _) = Identificar();
        var ate = DateTimeOffset.UtcNow.AddDays(IDadosContaUseCase.DiasAdiarLembrete);
        await _db.Tenants.Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LembreteWhatsAppAdiadoAte, ate), cancellationToken);
    }

    private (Guid TenantId, Guid UsuarioId) Identificar() =>
        _currentTenant.TenantId is { } t && _currentUser.UserId is { } u
            ? (t, u)
            : throw new InvalidOperationException("Conta ou usuário não identificado.");

    private async Task<bool> EhAdministradorAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var grupoId = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
            .Select(u => u.GrupoId)
            .FirstOrDefaultAsync(cancellationToken);
        return grupoId is { } id && await _db.Grupos.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(g => g.Id == id && g.TenantId == tenantId && g.EhAdministrador, cancellationToken);
    }
}
