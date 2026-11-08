using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Legal;

/// <summary>
/// Aviso prévio por e-mail da versão nova dos Termos e da Política (Termos,
/// item 15: 15 dias antes). Um e-mail por usuário e por versão
/// (ChaveDeduplicacao), só para quem tem e-mail confirmado, está ativo e
/// ainda não aceitou a versão nova. Chamado no escopo da conta (Tenant
/// definido), como os outros avisos.
/// </summary>
public sealed class AvisoNovosTermosService
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEmailQueueService _emailQueue;
    private readonly EmailOptions _emailOptions;

    public AvisoNovosTermosService(AppDbContext db, ICurrentTenant currentTenant, IEmailQueueService emailQueue, IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _currentTenant = currentTenant;
        _emailQueue = emailQueue;
        _emailOptions = emailOptions.Value;
    }

    /// <summary>Chave de deduplicação do aviso de um usuário para a versão atual.</summary>
    public static string Chave(Guid usuarioId) =>
        $"termos:{DocumentosLegais.VersaoTermosDeUso}/{DocumentosLegais.VersaoPoliticaPrivacidade}:{usuarioId:N}";

    /// <returns>Quantos e-mails foram para a fila agora.</returns>
    public async Task<int> AvisarContaAsync(CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            throw new InvalidOperationException("Tenant não definido para o aviso de novos termos.");

        var agora = DateTimeOffset.UtcNow;
        var usuarios = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId
                        && u.EmailConfirmed
                        && u.Email != null
                        && (u.ConvidadoEm == null || u.ConviteAceitoEm != null)
                        && (u.LockoutEnd == null || u.LockoutEnd < agora)
                        && !_db.AceitesTermos.Any(a => a.UsuarioId == u.Id
                                                       && a.VersaoTermosDeUso == DocumentosLegais.VersaoTermosDeUso
                                                       && a.VersaoPoliticaPrivacidade == DocumentosLegais.VersaoPoliticaPrivacidade))
            .Select(u => new { u.Id, Email = u.Email! })
            .ToListAsync(cancellationToken);

        var (assunto, corpo) = Mensagem();
        var enviados = 0;
        foreach (var usuario in usuarios)
        {
            var chave = Chave(usuario.Id);
            if (await _emailQueue.JaEnfileiradoAsync(chave, cancellationToken))
                continue;

            await _emailQueue.EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.NovosTermos, usuario.Id, usuario.Email.Trim(), assunto, corpo, ChaveDeduplicacao: chave),
                cancellationToken);
            enviados++;
        }
        return enviados;
    }

    private (string Assunto, string CorpoHtml) Mensagem()
    {
        var data = DocumentosLegais.VigentesDesde.ToString("dd/MM/yyyy");
        var baseUrl = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? "https://" + DocumentosLegais.Site
            : _emailOptions.AppBaseUrl.TrimEnd('/');

        var assunto = $"Termos de uso e Política de privacidade novos a partir de {data}";
        var corpo = $"""
            <p>Olá,</p>
            <p>Os <strong>Termos de uso</strong> e a <strong>Política de privacidade</strong> do {DocumentosLegais.NomeServico} têm uma versão nova ({DocumentosLegais.VersaoTermosDeUso}), que passa a valer em <strong>{data}</strong>.</p>
            <p>{WebUtility.HtmlEncode(DocumentosLegais.ResumoMudancas)}</p>
            <p>Leia os textos (o que mudou está no topo de cada um): <a href="{baseUrl}/termos">Termos de uso</a> · <a href="{baseUrl}/privacidade">Política de privacidade</a>.</p>
            <p>Você pode aceitar agora pelo aviso no topo da plataforma. A partir de {data}, o aceite será pedido ao entrar. Se não concordar, pode cancelar a assinatura e pedir o encerramento da conta, sem multa.</p>
            <p style="color:#64748b;font-size:12px">Aviso da plataforma {DocumentosLegais.NomeServico}, enviado a todos os usuários com e-mail confirmado. Dúvidas: {DocumentosLegais.EmailPrivacidade}.</p>
            """;
        return (assunto, corpo);
    }
}
