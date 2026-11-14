using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
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

    public Task<bool> AceitouVersaoVigenteAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        AceitouAoMenosAsync(usuarioId, DocumentosLegais.Vigente(DataBrasilia.Hoje()).Versao, cancellationToken);

    public Task<bool> AceitouVersaoAtualAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        AceitouAoMenosAsync(usuarioId, DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao, cancellationToken);

    /// <summary>
    /// Aceitou esta versão ou uma mais nova (aceitar a 1.2 vale para a 1.1).
    /// Termos e Política andam juntos: vale a menor das duas versões aceitas
    /// no mesmo registro.
    /// </summary>
    private async Task<bool> AceitouAoMenosAsync(Guid usuarioId, string versao, CancellationToken cancellationToken)
    {
        var chave = ChaveCache(usuarioId, versao);
        if (_cache.TryGetValue(chave, out _))
            return true;

        var aceites = await _db.AceitesTermos.AsNoTracking()
            .Where(a => a.UsuarioId == usuarioId)
            .Select(a => new { a.VersaoTermosDeUso, a.VersaoPoliticaPrivacidade })
            .ToListAsync(cancellationToken);

        var minimo = DocumentosLegais.Ordem(versao);
        var aceitou = minimo >= 0 && aceites.Any(a =>
            Math.Min(DocumentosLegais.Ordem(a.VersaoTermosDeUso), DocumentosLegais.Ordem(a.VersaoPoliticaPrivacidade)) >= minimo);

        // Só a resposta positiva vai pro cache (quem não aceitou consulta de novo até aceitar).
        if (aceitou)
            _cache.Set(chave, true, DuracaoCache);

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
            VersaoTermosDeUso = DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao,
            VersaoPoliticaPrivacidade = DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao,
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

    public void MarcarComoAceito(Guid usuarioId)
    {
        // Aceitou a atual: vale também para a vigente (que é igual ou mais antiga).
        var hoje = DataBrasilia.Hoje();
        _cache.Set(ChaveCache(usuarioId, DocumentosLegais.Atual(hoje).Versao), true, DuracaoCache);
        _cache.Set(ChaveCache(usuarioId, DocumentosLegais.Vigente(hoje).Versao), true, DuracaoCache);
    }

    private static string ChaveCache(Guid usuarioId, string versao) => $"aceite-termos:{usuarioId:N}:{versao}";
}
