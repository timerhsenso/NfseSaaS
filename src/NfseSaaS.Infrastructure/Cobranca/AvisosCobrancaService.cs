using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Avisos de cobrança (e-mail) e faixa do painel. As decisões ficam em
/// RegrasAvisoCobranca; aqui só banco e fila de e-mail.
///
/// Respeita Cobranca:Ativa, contas piloto e contas isentas: com a cobrança
/// desligada para a conta, nada é enviado e a faixa não aparece.
/// Deduplicação: EmailLog.ChaveDeduplicacao (a chave vem das regras).
/// </summary>
public sealed class AvisosCobrancaService : IEnviarAvisosCobrancaUseCase, IObterFaixaCobrancaUseCase
{
    // O job diário e o disparo pelo webhook podem cair juntos na mesma conta:
    // a conferência "já avisado?" e a gravação precisam acontecer uma por vez.
    private static readonly SemaphoreSlim Trava = new(1, 1);

    public const string MotivoSemDestinatario = "Nenhum administrador com e-mail confirmado.";

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEmailQueueService _emailQueue;
    private readonly ICatalogoPlanos _catalogo;
    private readonly CobrancaOptions _cobranca;
    private readonly EmailOptions _email;
    private readonly SuporteOptions _suporte;
    private readonly ILogger<AvisosCobrancaService> _logger;

    public AvisosCobrancaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        IEmailQueueService emailQueue,
        ICatalogoPlanos catalogo,
        IOptions<CobrancaOptions> cobranca,
        IOptions<EmailOptions> email,
        IOptions<SuporteOptions> suporte,
        ILogger<AvisosCobrancaService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _emailQueue = emailQueue;
        _catalogo = catalogo;
        _cobranca = cobranca.Value;
        _email = email.Value;
        _suporte = suporte.Value;
        _logger = logger;
    }

    /// <summary>A cobrança vale para a conta (ligada ou piloto) e ela não é isenta.</summary>
    public bool CobrancaValePara(Guid tenantId) =>
        _cobranca.AtivaPara(tenantId) && !_cobranca.ContasIsentas.Contains(tenantId);

    // ---------- E-mails ----------

    public async Task<AvisosCobrancaResult> ExecutarAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId != tenantId)
            throw new InvalidOperationException("O Tenant da conta precisa estar definido para os avisos de cobrança.");

        if (!CobrancaValePara(tenantId))
            return Nada("Cobrança desligada ou conta isenta.");

        await Trava.WaitAsync(cancellationToken);
        try
        {
            var assinatura = await _db.Assinaturas.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
            if (assinatura is null)
                return Nada("Conta sem assinatura.");

            var estado = await EstadoAsync(assinatura, cancellationToken);
            var devidos = RegrasAvisoCobranca.Avaliar(estado, DateTimeOffset.UtcNow, DataBrasilia.Hoje());
            if (devidos.Count == 0)
                return Nada("Nenhum aviso devido.");

            var pendentes = new List<AvisoCobranca>();
            foreach (var aviso in devidos)
            {
                if (!await _emailQueue.JaEnfileiradoAsync(aviso.Chave, cancellationToken))
                    pendentes.Add(aviso);
            }

            if (pendentes.Count == 0)
                return Nada("Avisos já enviados.");

            var destinatarios = await DestinatariosAviso.AdministradoresConfirmadosDaContaAsync(_db, tenantId, cancellationToken);
            if (destinatarios.Count == 0)
                return new AvisosCobrancaResult(Array.Empty<TipoAvisoCobranca>(), destinatarios, MotivoSemDestinatario);

            var planoNome = assinatura.Plano is { } codigo ? (await _catalogo.ObterAsync(codigo, cancellationToken))?.Nome : null;
            var link = string.IsNullOrWhiteSpace(_email.AppBaseUrl) ? null : $"{_email.AppBaseUrl.TrimEnd('/')}/Assinatura";

            foreach (var aviso in pendentes)
            {
                var (assunto, corpoHtml) = MensagensAvisoCobranca.Montar(aviso, estado, planoNome, link, _suporte.TextoContato);
                foreach (var destinatario in destinatarios)
                {
                    await _emailQueue.EnfileirarAsync(
                        new EmailParaEnfileirar(TipoEmailCatalogo.AvisoCobranca, null, destinatario, assunto, corpoHtml, ChaveDeduplicacao: aviso.Chave),
                        cancellationToken);
                }
            }

            _logger.LogInformation("Avisos de cobrança da conta {TenantId}: {Avisos} para {Destinatarios}.",
                tenantId, string.Join(", ", pendentes.Select(a => a.Tipo)), string.Join(", ", destinatarios));

            return new AvisosCobrancaResult(pendentes.Select(a => a.Tipo).ToList(), destinatarios, null);
        }
        finally
        {
            Trava.Release();
        }
    }

    // ---------- Faixa do painel ----------

    async Task<FaixaCobrancaResponse?> IObterFaixaCobrancaUseCase.ExecutarAsync(bool incluirLinkPagamento, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId || !CobrancaValePara(tenantId))
            return null;

        // Só leitura: a assinatura nasce na tela Assinatura ou na emissão, não aqui.
        var assinatura = await _db.Assinaturas.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        if (assinatura is null)
            return null;

        var estado = await EstadoAsync(assinatura, cancellationToken);
        var faixa = RegrasAvisoCobranca.Faixa(estado, DateTimeOffset.UtcNow, DataBrasilia.Hoje());
        if (faixa is null)
            return null;

        var nivel = faixa.Nivel switch
        {
            NivelFaixaCobranca.Bloqueio => "bloqueio",
            NivelFaixaCobranca.Atencao => "atencao",
            _ => "info"
        };
        var url = incluirLinkPagamento && faixa.MostrarPagar ? assinatura.UrlFaturaPendente : null;
        return new FaixaCobrancaResponse(nivel, faixa.Mensagem, url, faixa.SugerirTroca);
    }

    // ---------- Apoio ----------

    private async Task<EstadoAvisoCobranca> EstadoAsync(Assinatura a, CancellationToken cancellationToken) =>
        new(a.TenantId, a.Status, a.TesteIniciadoEm, a.PrimeiraCobrancaEm,
            a.AsaasAssinaturaId, a.AtrasadaDesde, a.UrlFaturaPendente, a.VencimentoFaturaPendente, a.ValorFaturaPendente,
            a.UltimoPagamentoEm, a.CanceladaEm,
            await UsoAsync(a, cancellationToken),
            a.AcessoAteAposCancelar,
            CobrancaService.ReservaValida(a, DateTimeOffset.UtcNow),
            a.SemTesteGratis);

    /// <summary>Uso frente ao plano, só para quem assinou um plano (não cancelado).</summary>
    private async Task<UsoPlano?> UsoAsync(Assinatura a, CancellationToken cancellationToken)
    {
        if (a.Plano is not { } codigo || a.AsaasAssinaturaId is null || a.Status == Domain.Enums.StatusAssinatura.Cancelada)
            return null;

        var plano = await _catalogo.ObterAsync(codigo, cancellationToken);
        if (plano is null)
            return null;

        return new UsoPlano(plano.Nome, plano.MaxEmpresas, plano.NotasPorMes,
            await CobrancaService.ContarEmpresasAtivasAsync(_db, a.TenantId, cancellationToken),
            await CobrancaService.ContarNotasDoMesAsync(_db, a.TenantId, cancellationToken));
    }

    private static AvisosCobrancaResult Nada(string motivo) =>
        new(Array.Empty<TipoAvisoCobranca>(), Array.Empty<string>(), motivo);
}
