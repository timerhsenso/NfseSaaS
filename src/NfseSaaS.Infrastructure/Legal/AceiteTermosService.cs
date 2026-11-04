using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Legal;

/// <summary>
/// Implementação de IAceiteTermosService. A checagem roda em toda
/// requisição autenticada (ver ExigeAceiteTermosFilter no Web), então só a
/// resposta POSITIVA vai pro cache: quem ainda não aceitou consulta o banco
/// de novo a cada tela, o que só acontece até ele aceitar. A chave inclui
/// as versões vigentes, então subir a versão invalida o cache sozinho
/// (depois de um deploy, o processo novo já começa com o cache vazio).
/// </summary>
public sealed class AceiteTermosService : IAceiteTermosService
{
    private static readonly TimeSpan DuracaoCache = TimeSpan.FromMinutes(30);

    private const int TamanhoMaximoUserAgent = 500;

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AceiteTermosService(
        AppDbContext db,
        IMemoryCache cache,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _cache = cache;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> AceitouVersaoVigenteAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ChaveCache(usuarioId), out _))
            return true;

        var aceitou = await _db.AceitesTermos.AsNoTracking().AnyAsync(a =>
            a.UsuarioId == usuarioId
            && a.VersaoTermosDeUso == DocumentosLegais.VersaoTermosDeUso
            && a.VersaoPoliticaPrivacidade == DocumentosLegais.VersaoPoliticaPrivacidade,
            cancellationToken);

        if (aceitou)
            MarcarComoAceito(usuarioId);

        return aceitou;
    }

    public void Registrar(Guid usuarioId, string origem)
    {
        var userAgent = _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
            userAgent = null;
        else if (userAgent.Length > TamanhoMaximoUserAgent)
            userAgent = userAgent[..TamanhoMaximoUserAgent];

        var aceite = new AceiteTermos
        {
            UsuarioId = usuarioId,
            VersaoTermosDeUso = DocumentosLegais.VersaoTermosDeUso,
            VersaoPoliticaPrivacidade = DocumentosLegais.VersaoPoliticaPrivacidade,
            AceitoEm = DateTimeOffset.UtcNow,
            IpAddress = _currentUser.IpAddress,
            UserAgent = userAgent,
            Origem = origem
        };

        _db.AceitesTermos.Add(aceite);

        _auditLogWriter.Registrar("AceitarTermos", "ApplicationUser", usuarioId, new
        {
            aceite.VersaoTermosDeUso,
            aceite.VersaoPoliticaPrivacidade,
            origem
        });
    }

    public void MarcarComoAceito(Guid usuarioId) =>
        _cache.Set(ChaveCache(usuarioId), true, DuracaoCache);

    private static string ChaveCache(Guid usuarioId) =>
        $"aceite-termos:{usuarioId:N}:{DocumentosLegais.VersaoTermosDeUso}:{DocumentosLegais.VersaoPoliticaPrivacidade}";
}
