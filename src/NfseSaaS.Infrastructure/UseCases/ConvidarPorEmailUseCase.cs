using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Convite por e-mail da tela "Indique e ganhe" (regras em
/// RegrasConviteIndicacao). O e-mail sai pela fila, com Reply-To de quem
/// convidou e o link de indicação da conta (?ref=…&amp;origem=convite-email).
/// Quem convida precisa ter o e-mail confirmado (ele vira o Reply-To).
/// </summary>
public sealed class ConvidarPorEmailUseCase : IConvidarPorEmailUseCase
{
    // Conferência do limite diário e gravação, um envio por vez no processo
    // (dois cliques não passam do limite).
    private static readonly SemaphoreSlim Trava = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<ConvidarPorEmailUseCase> _logger;

    public ConvidarPorEmailUseCase(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IEmailQueueService emailQueue,
        IAuditLogWriter auditLogWriter,
        ILogger<ConvidarPorEmailUseCase> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    /// <summary>Início do dia de hoje em Brasília, em UTC.</summary>
    internal static DateTimeOffset InicioDoDiaUtc()
    {
        var hoje = DataBrasilia.Hoje();
        return new DateTimeOffset(hoje.Year, hoje.Month, hoje.Day, 0, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
    }

    /// <summary>Convites que a conta ainda pode mandar hoje.</summary>
    internal static async Task<int> RestamHojeAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var inicio = InicioDoDiaUtc();
        var enviados = await db.ConvitesIndicacao.IgnoreQueryFilters()
            .CountAsync(c => c.TenantId == tenantId && c.CreatedAt >= inicio, cancellationToken);
        return Math.Max(0, RegrasConviteIndicacao.LimitePorDia - enviados);
    }

    public async Task<ConvidarPorEmailResponse> ExecutarAsync(ConvidarPorEmailRequest request, string linkIndicacao, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId || _currentUser.UserId is not { } usuarioId)
            throw new InvalidOperationException("Conta ou usuário não identificado para o convite.");

        var (validos, invalidos) = RegrasConviteIndicacao.Separar(request.Emails);
        if (validos.Count + invalidos.Count == 0)
            throw new RegraNegocioException("Informe pelo menos um e-mail.");
        if (validos.Count + invalidos.Count > RegrasConviteIndicacao.MaximoPorEnvio)
            throw new RegraNegocioException($"Envie até {RegrasConviteIndicacao.MaximoPorEnvio} e-mails por vez.");

        var remetente = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
            .Select(u => new { u.Email, u.EmailConfirmed })
            .FirstAsync(cancellationToken);
        if (!remetente.EmailConfirmed || string.IsNullOrWhiteSpace(remetente.Email))
            throw new RegraNegocioException("Confirme o seu e-mail antes de enviar convites (as respostas dos convidados vão para ele).");

        var conta = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.RazaoSocial).FirstAsync(cancellationToken);
        var link = RegrasConviteIndicacao.Link(linkIndicacao);
        var emailRemetente = remetente.Email.Trim().ToLowerInvariant();

        await Trava.WaitAsync(cancellationToken);
        try
        {
            var restam = await RestamHojeAsync(_db, tenantId, cancellationToken);
            if (restam == 0)
                throw new RegraNegocioException($"Você já enviou os {RegrasConviteIndicacao.LimitePorDia} convites de hoje. Tente de novo amanhã.");

            // Bloqueados: já têm usuário (qualquer conta) ou receberam convite há menos de 30 dias (de qualquer conta).
            var desde = DateTimeOffset.UtcNow.AddDays(-RegrasConviteIndicacao.IntervaloMesmoEmailDias);
            var normalizados = validos.Select(e => e.ToUpperInvariant()).ToList();
            var jaUsuarios = await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.NormalizedEmail != null && normalizados.Contains(u.NormalizedEmail))
                .Select(u => u.NormalizedEmail!)
                .ToListAsync(cancellationToken);
            var jaConvidados = await _db.ConvitesIndicacao.IgnoreQueryFilters().AsNoTracking()
                .Where(c => validos.Contains(c.Email) && c.CreatedAt >= desde)
                .Select(c => c.Email)
                .ToListAsync(cancellationToken);

            var resultados = invalidos.Select(e => new ResultadoConvite(e, false, RegrasConviteIndicacao.MotivoInvalido)).ToList();
            var (assunto, corpo) = MensagemConviteIndicacao.Montar(conta, emailRemetente, link);
            var enviados = 0;

            foreach (var email in validos)
            {
                string? motivo = email == emailRemetente ? RegrasConviteIndicacao.MotivoProprio
                    : jaUsuarios.Contains(email.ToUpperInvariant()) || jaConvidados.Contains(email) ? RegrasConviteIndicacao.MotivoBloqueado
                    : restam == 0 ? RegrasConviteIndicacao.MotivoLimite
                    : null;

                if (motivo is not null)
                {
                    resultados.Add(new ResultadoConvite(email, false, motivo));
                    continue;
                }

                var emailLogId = await _emailQueue.EnfileirarAsync(
                    new EmailParaEnfileirar(TipoEmailCatalogo.ConviteIndicacao, usuarioId, email, assunto, corpo, ResponderPara: emailRemetente),
                    cancellationToken);
                _db.ConvitesIndicacao.Add(new ConviteIndicacao { Email = email, EnviadoPorUsuarioId = usuarioId, EmailLogId = emailLogId });
                resultados.Add(new ResultadoConvite(email, true, null));
                restam--;
                enviados++;
            }

            if (enviados > 0)
            {
                _auditLogWriter.Registrar("ConvidarPorEmail", "Indicacao", tenantId, new { enviados });
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Conta {TenantId} enviou {Enviados} convite(s) de indicação.", tenantId, enviados);
            }

            return new ConvidarPorEmailResponse(resultados, restam);
        }
        finally
        {
            Trava.Release();
        }
    }
}

/// <summary>Texto do convite (fixo, sem mensagem livre). Separado para teste.</summary>
public static class MensagemConviteIndicacao
{
    public static (string Assunto, string CorpoHtml) Montar(string conta, string emailRemetente, string link)
    {
        var nome = WebUtility.HtmlEncode(conta);
        var remetente = WebUtility.HtmlEncode(emailRemetente);
        var assunto = $"{Limitar(conta, 120)} indicou o micoempresa para você";

        var corpo = $"""
            <p>Olá!</p>
            <p><strong>{nome}</strong> ({remetente}) usa o <strong>micoempresa</strong> para emitir notas fiscais de serviço e indicou para você.</p>
            <ul>
              <li>Emissão pelo Emissor Nacional da NFS-e, com o certificado da sua empresa;</li>
              <li>nota mensal automática para contratos, com reajuste por índice;</li>
              <li>o cliente recebe a nota (PDF e XML) por e-mail;</li>
              <li>rejeição da SEFIN explicada em português, com o que corrigir.</li>
            </ul>
            <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;padding:10px 18px;background:#2563eb;color:#fff;border-radius:6px;text-decoration:none;font-weight:600">Criar conta grátis</a></p>
            <p>Dúvidas sobre a indicação? Responda este e-mail: a resposta vai direto para {remetente}.</p>
            <p style="color:#64748b;font-size:12px">Você recebeu este convite porque {nome} informou o seu e-mail na plataforma micoempresa. Não vamos mandar outros e-mails a partir dele.</p>
            """;

        return (assunto, corpo);
    }

    private static string Limitar(string texto, int max) => texto.Length <= max ? texto : texto[..max];
}
