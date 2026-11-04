using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Painel do operador: todas as contas com os números de uso. Lê por cima
/// do filtro de tenant (IgnoreQueryFilters) de propósito — quem chama é só
/// o operador da plataforma (RequerOperadorPlataforma na API).
/// </summary>
public sealed class ListarTenantsPlataformaUseCase : IListarTenantsPlataformaUseCase
{
    private readonly AppDbContext _db;

    public ListarTenantsPlataformaUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TenantPlataformaResponse>> ExecutarAsync(CancellationToken cancellationToken)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .OrderBy(t => t.RazaoSocial)
            .ToListAsync(cancellationToken);

        var usuarios = await _db.Users.AsNoTracking()
            .GroupBy(u => u.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Total = g.Count(),
                // Quem se cadastrou (não foi convidado) é o responsável pela conta.
                Responsavel = g.Where(u => u.ConvidadoEm == null).OrderBy(u => u.Email).Select(u => u.Email).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.TenantId, cancellationToken);

        var empresas = await _db.Empresas.AsNoTracking().IgnoreQueryFilters()
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Total = g.Count(), Producao = g.Count(e => e.TipoAmbiente == TipoAmbiente.Producao) })
            .ToDictionaryAsync(x => x.TenantId, cancellationToken);

        // Mês corrente no horário de Brasília, em UTC (coluna timestamptz).
        var agoraBrasilia = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
        var inicioMesUtc = new DateTimeOffset(agoraBrasilia.Year, agoraBrasilia.Month, 1, 0, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();

        var notas = await _db.NotasFiscais.AsNoTracking().IgnoreQueryFilters()
            .Where(n => n.Status == NfseStatus.Autorizada)
            .GroupBy(n => n.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Total = g.Count(),
                NoMes = g.Count(n => n.DataEmissao >= inicioMesUtc),
                Ultima = g.Max(n => n.DataEmissao)
            })
            .ToDictionaryAsync(x => x.TenantId, cancellationToken);

        return tenants.Select(t =>
        {
            usuarios.TryGetValue(t.Id, out var u);
            empresas.TryGetValue(t.Id, out var e);
            notas.TryGetValue(t.Id, out var n);
            return new TenantPlataformaResponse(
                t.Id, t.RazaoSocial, t.NomeFantasia, t.Cnpj, t.Ativo, t.CreatedAt, t.BloqueadoEm, t.MotivoBloqueio,
                u?.Responsavel, u?.Total ?? 0, e?.Total ?? 0, e?.Producao ?? 0, n?.NoMes ?? 0, n?.Total ?? 0, n?.Ultima);
        }).ToList();
    }
}

/// <summary>
/// Suspender: Tenant.Ativo=false (login recusado — SignInManagerComTenant;
/// emissão recusada — EmitirNfseUseCase; jobs pulam a conta) e troca o
/// SecurityStamp de todos os usuários da conta, o que derruba as sessões
/// abertas na próxima revalidação do cookie (até 1 minuto). Reativar desfaz
/// a suspensão. Fica registrado no Histórico (AuditLog) da própria conta.
/// </summary>
public sealed class AlterarBloqueioTenantUseCase : IAlterarBloqueioTenantUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<AlterarBloqueioTenantUseCase> _logger;

    public AlterarBloqueioTenantUseCase(
        AppDbContext db, IAuditLogWriter auditLogWriter, ICurrentTenant currentTenant, ILogger<AlterarBloqueioTenantUseCase> logger)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task ExecutarAsync(Guid tenantId, bool suspender, string? motivo, Guid? operadorId, CancellationToken cancellationToken)
    {
        if (suspender && tenantId == _currentTenant.TenantId)
            throw new RegraNegocioException("Você não pode suspender a própria conta da plataforma.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Conta {tenantId} não encontrada.");

        if (tenant.Ativo == !suspender)
            throw new RegraNegocioException(suspender ? "Esta conta já está suspensa." : "Esta conta já está ativa.");

        tenant.Ativo = !suspender;
        tenant.BloqueadoEm = suspender ? DateTimeOffset.UtcNow : null;
        tenant.MotivoBloqueio = suspender ? motivo?.Trim() : null;

        // O AuditLog é da conta afetada (o cliente vê no Histórico dela).
        _db.TenantIdOverrideDeSistema = tenantId;
        try
        {
            _auditLogWriter.Registrar(suspender ? "ContaSuspensaPelaPlataforma" : "ContaReativadaPelaPlataforma", "Tenant", tenantId,
                new { Motivo = tenant.MotivoBloqueio, OperadorId = operadorId });
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }

        if (suspender)
        {
            // Stamp novo para todos: a revalidação do cookie (1 min) não
            // reconhece mais o stamp antigo e desloga.
            var novoStamp = Guid.NewGuid().ToString("N").ToUpperInvariant();
            await _db.Users
                .Where(u => u.TenantId == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, novoStamp), cancellationToken);
        }

        _logger.LogWarning("Conta {TenantId} ({RazaoSocial}) {Acao} pelo operador {OperadorId}. Motivo: {Motivo}",
            tenantId, tenant.RazaoSocial, suspender ? "SUSPENSA" : "reativada", operadorId, tenant.MotivoBloqueio);
    }
}
