using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Aparencia;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Aparência (painel lateral do topo): paleta da conta, escolhida só pelo
/// administrador (grupo administrador) e válida para todos; preferências
/// de cada usuário (texto, botões e campos, modo, menu). Fica no banco para
/// valer em qualquer navegador e já vir no HTML (sem a tela "piscar").
/// O layout lê em toda página: por isso o cache curto, limpo a cada gravação.
/// </summary>
public sealed class AparenciaService : IAparenciaUseCase
{
    private static readonly TimeSpan DuracaoCache = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IMemoryCache _cache;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<AparenciaService> _logger;

    public AparenciaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IMemoryCache cache,
        IAuditLogWriter auditLogWriter,
        ILogger<AparenciaService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _cache = cache;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    private sealed record DadosUsuario(string? PreferenciasJson, bool EhAdministrador);

    internal static string ChaveConta(Guid tenantId) => $"aparencia:conta:{tenantId:N}";
    internal static string ChaveUsuario(Guid usuarioId) => $"aparencia:usuario:{usuarioId:N}";

    public async Task<AparenciaAtual> AtualAsync(CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId || _currentUser.UserId is not { } usuarioId)
            return new AparenciaAtual(AparenciaCatalogo.PaletaPadrao, PreferenciasAparencia.Padrao, Salvo: true, PodeAlterarPaleta: false);

        try
        {
            var paleta = await PaletaAsync(tenantId, cancellationToken);
            var usuario = await UsuarioAsync(tenantId, usuarioId, cancellationToken);
            return new AparenciaAtual(paleta, PreferenciasAparencia.Ler(usuario.PreferenciasJson),
                usuario.PreferenciasJson is not null, usuario.EhAdministrador);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A aparência nunca pode derrubar a página.
            _logger.LogWarning(ex, "Falha ao ler a aparência da conta {TenantId}; usando o padrão.", tenantId);
            return new AparenciaAtual(AparenciaCatalogo.PaletaPadrao, PreferenciasAparencia.Padrao, Salvo: true, PodeAlterarPaleta: false);
        }
    }

    public async Task<AparenciaResponse> ObterAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        var usuario = await UsuarioAsync(tenantId, usuarioId, cancellationToken);
        return new AparenciaResponse(
            await PaletaAsync(tenantId, cancellationToken),
            usuario.EhAdministrador,
            PreferenciasAparencia.Ler(usuario.PreferenciasJson),
            AparenciaCatalogo.Paletas, AparenciaCatalogo.Fontes, AparenciaCatalogo.Densidades, AparenciaCatalogo.Modos);
    }

    public async Task<PreferenciasAparencia> SalvarPreferenciasAsync(PreferenciasAparencia preferencias, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        var validas = PreferenciasAparencia.Validar(preferencias);

        var usuario = await _db.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Id == usuarioId && u.TenantId == tenantId, cancellationToken);
        usuario.PreferenciasAparencia = validas.Serializar();
        await _db.SaveChangesAsync(cancellationToken);

        _cache.Remove(ChaveUsuario(usuarioId));
        return validas;
    }

    public async Task SalvarPaletaAsync(SalvarPaletaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        if (!(await UsuarioAsync(tenantId, usuarioId, cancellationToken)).EhAdministrador)
            throw new RegraNegocioException("Só o administrador da conta muda as cores do sistema.");
        if (!AparenciaCatalogo.PaletaValida(request.Paleta))
            throw new RegraNegocioException("Paleta de cores inválida.");

        var conta = await _db.Tenants.FirstAsync(t => t.Id == tenantId, cancellationToken);
        var anterior = AparenciaCatalogo.PaletaOuPadrao(conta.PaletaCores);
        // A padrão grava nulo: a conta segue a padrão mesmo se ela mudar de nome no futuro.
        conta.PaletaCores = request.Paleta == AparenciaCatalogo.PaletaPadrao ? null : request.Paleta;
        _auditLogWriter.Registrar("AlterarPaletaCores", "Tenant", tenantId, new { de = anterior, para = request.Paleta });
        await _db.SaveChangesAsync(cancellationToken);

        _cache.Remove(ChaveConta(tenantId));
    }

    private (Guid TenantId, Guid UsuarioId) Identificar() =>
        _currentTenant.TenantId is { } t && _currentUser.UserId is { } u
            ? (t, u)
            : throw new InvalidOperationException("Conta ou usuário não identificado.");

    private async Task<string> PaletaAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await _cache.GetOrCreateAsync(ChaveConta(tenantId), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = DuracaoCache;
            var gravada = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.PaletaCores).FirstOrDefaultAsync(cancellationToken);
            return AparenciaCatalogo.PaletaOuPadrao(gravada);
        }))!;

    private async Task<DadosUsuario> UsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken) =>
        (await _cache.GetOrCreateAsync(ChaveUsuario(usuarioId), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = DuracaoCache;
            var usuario = await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
                .Select(u => new { u.PreferenciasAparencia, u.GrupoId })
                .FirstOrDefaultAsync(cancellationToken);
            var ehAdmin = usuario?.GrupoId is { } grupoId && await _db.Grupos.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(g => g.Id == grupoId && g.TenantId == tenantId && g.EhAdministrador, cancellationToken);
            return new DadosUsuario(usuario?.PreferenciasAparencia, ehAdmin);
        }))!;
}
