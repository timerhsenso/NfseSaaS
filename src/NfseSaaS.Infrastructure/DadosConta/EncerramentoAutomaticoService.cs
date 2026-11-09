using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.DadosConta;

/// <summary>
/// Configuração do encerramento automático (D2, parte 2). Vazio = DESLIGADO
/// (padrão): os Termos 1.1 prometem a conta disponível depois de cancelar.
/// </summary>
public sealed class EncerramentoOptions
{
    public const string SectionName = "Encerramento";

    /// <summary>Dias depois do fim do acesso (ou do cancelamento) para encerrar a conta. Vazio ou 0 = desligado; menos de 30 vira 30.</summary>
    public int? AutomaticoAposCancelarDias { get; set; }

    public bool Ligado => AutomaticoAposCancelarDias is > 0;
}

/// <summary>
/// Job diário: contas com a assinatura cancelada recebem dois avisos por
/// e-mail (com a data e os caminhos "exporte seus dados" e "assine de novo")
/// e, na data prometida, são encerradas pelo mesmo caminho do encerramento a
/// pedido (EncerramentoContaService, origem Automatico). Fica de fora:
/// conta isenta, conta de operador, conta suspensa ou já encerrada, e conta
/// com a cobrança desligada (beta). Sem destinatário para avisar, não encerra.
/// </summary>
public sealed class EncerramentoAutomaticoService : IEncerrarContasCanceladasUseCase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly EncerramentoContaService _encerramento;
    private readonly IEmailQueueService _emailQueue;
    private readonly EncerramentoOptions _options;
    private readonly CobrancaOptions _cobranca;
    private readonly PlataformaOptions _plataforma;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<EncerramentoAutomaticoService> _logger;

    public EncerramentoAutomaticoService(
        AppDbContext db,
        EncerramentoContaService encerramento,
        IEmailQueueService emailQueue,
        IOptions<EncerramentoOptions> options,
        IOptions<CobrancaOptions> cobranca,
        IOptions<PlataformaOptions> plataforma,
        IOptions<EmailOptions> emailOptions,
        ILogger<EncerramentoAutomaticoService> logger)
    {
        _db = db;
        _encerramento = encerramento;
        _emailQueue = emailQueue;
        _options = options.Value;
        _cobranca = cobranca.Value;
        _plataforma = plataforma.Value;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task<ResultadoEncerramentoAutomatico> ExecutarAsync(DateOnly hoje, CancellationToken cancellationToken)
    {
        if (!_options.Ligado)
            return new ResultadoEncerramentoAutomatico(true, 0, 0, 0, 0);

        var dias = _options.AutomaticoAposCancelarDias!.Value;
        if (dias < RegrasEncerramentoAutomatico.DiasMinimos)
            _logger.LogWarning("Encerramento:AutomaticoAposCancelarDias = {Dias} é menor que o mínimo; usando {Minimo}.", dias, RegrasEncerramentoAutomatico.DiasMinimos);

        var candidatas = await (
                from a in _db.Assinaturas.AsNoTracking()
                join t in _db.Tenants.AsNoTracking() on a.TenantId equals t.Id
                where a.Status == StatusAssinatura.Cancelada && a.CanceladaEm != null && t.EncerradaEm == null && t.Ativo
                select new { a.TenantId, CanceladaEm = a.CanceladaEm!.Value, a.AcessoAteAposCancelar, a.IsentaPelaPlataforma, a.IsentaAte, t.RazaoSocial, t.NomeFantasia })
            .ToListAsync(cancellationToken);

        int avisadas = 0, encerradas = 0, semDestinatario = 0, comErro = 0;
        foreach (var c in candidatas)
        {
            if (!_cobranca.AtivaPara(c.TenantId)
                || RegrasCobranca.Isenta(_cobranca.ContasIsentas.Contains(c.TenantId), c.IsentaPelaPlataforma, c.IsentaAte, hoje)
                || await TemOperadorAsync(c.TenantId, cancellationToken))
                continue;

            try
            {
                var prevista = RegrasEncerramentoAutomatico.DataPrevista(c.CanceladaEm, c.AcessoAteAposCancelar, dias);
                var prefixo = RegrasEncerramentoAutomatico.ChavePrefixo(c.TenantId, c.CanceladaEm);
                var chaves = await _db.EmailLogs.IgnoreQueryFilters().AsNoTracking()
                    .Where(e => e.TenantId == c.TenantId && e.ChaveDeduplicacao != null && e.ChaveDeduplicacao.StartsWith(prefixo))
                    .Select(e => e.ChaveDeduplicacao!)
                    .ToListAsync(cancellationToken);
                var prometida = chaves.Select(RegrasEncerramentoAutomatico.DataNaChave).Where(d => d is not null).Min();
                var etapa = RegrasEncerramentoAutomatico.Etapa(hoje, prevista, prometida, chaves.Any(RegrasEncerramentoAutomatico.EhAvisoFinal));

                switch (etapa)
                {
                    case EtapaEncerramentoAutomatico.AvisoAntecipado:
                    case EtapaEncerramentoAutomatico.AvisoFinal:
                        var data = prometida ?? RegrasEncerramentoAutomatico.DataPrometida(prevista, hoje);
                        var enviados = await AvisarAsync(c.TenantId, c.NomeFantasia ?? c.RazaoSocial, c.CanceladaEm, data, etapa, prefixo, cancellationToken);
                        if (enviados == 0)
                        {
                            semDestinatario++;
                            _logger.LogWarning("Encerramento automático: a conta {TenantId} não tem administrador com e-mail; não será encerrada sem aviso.", c.TenantId);
                        }
                        else
                            avisadas++;
                        break;

                    case EtapaEncerramentoAutomatico.Encerrar:
                        var motivo = $"assinatura cancelada em {c.CanceladaEm.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", PtBr)}, aviso para {prometida!.Value.ToString("dd/MM/yyyy", PtBr)}";
                        await _encerramento.EncerrarAsync(c.TenantId, OrigemEncerramento.Automatico, motivo, usuarioId: null, cancellationToken);
                        encerradas++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                comErro++;
                _db.ChangeTracker.Clear();
                _db.TenantIdOverrideDeSistema = null;
                _logger.LogError(ex, "Encerramento automático da conta {TenantId} falhou; tenta de novo amanhã.", c.TenantId);
            }
        }

        if (avisadas + encerradas + semDestinatario + comErro > 0)
            _logger.LogWarning("Encerramento automático: {Avisadas} avisada(s), {Encerradas} encerrada(s), {SemDestinatario} sem destinatário, {ComErro} com erro.",
                avisadas, encerradas, semDestinatario, comErro);

        return new ResultadoEncerramentoAutomatico(false, avisadas, encerradas, semDestinatario, comErro);
    }

    /// <returns>Quantos e-mails entraram na fila.</returns>
    private async Task<int> AvisarAsync(Guid tenantId, string nomeConta, DateTimeOffset canceladaEm, DateOnly data, EtapaEncerramentoAutomatico etapa, string prefixo, CancellationToken cancellationToken)
    {
        var destinatarios = await (
            from u in _db.Users.AsNoTracking()
            join g in _db.Grupos.IgnoreQueryFilters().AsNoTracking() on u.GrupoId equals g.Id
            where u.TenantId == tenantId && g.TenantId == tenantId && g.EhAdministrador && u.Email != null
            select new { u.Id, Email = u.Email! }).ToListAsync(cancellationToken);
        destinatarios = destinatarios.DistinctBy(d => d.Email.Trim().ToLowerInvariant()).ToList();
        if (destinatarios.Count == 0)
            return 0;

        var baseUrl = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl) ? "https://" + DocumentosLegais.Site : _emailOptions.AppBaseUrl.TrimEnd('/');
        var quando = data.ToString("dd/MM/yyyy", PtBr);
        var cancelada = canceladaEm.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", PtBr);
        var url = WebUtility.HtmlEncode(baseUrl);
        var assunto = etapa == EtapaEncerramentoAutomatico.AvisoFinal
            ? $"Sua conta será encerrada em {quando}"
            : $"Aviso: sua conta será encerrada em {quando}";
        var corpo = $"""
            <p>Olá,</p>
            <p>A assinatura da conta <strong>{WebUtility.HtmlEncode(nomeConta)}</strong> no {DocumentosLegais.NomeServico} foi cancelada em {cancelada}. Por isso, em <strong>{quando}</strong> a conta será <strong>encerrada</strong>: o acesso fecha e, {RegrasEncerramentoConta.DiasAteExpurgo} dias depois, os dados são apagados (as notas fiscais ficam guardadas pelo prazo legal).</p>
            <p>Antes dessa data você pode:</p>
            <ul>
              <li><a href="{url}/MeusDados">exportar os seus dados</a> (notas em XML e PDF, cadastros e documentos);</li>
              <li><a href="{url}/Assinatura">assinar de novo</a> — com a assinatura ativa, a conta não é encerrada.</li>
            </ul>
            <p style="color:#64748b;font-size:12px">Dúvidas: {DocumentosLegais.EmailPrivacidade}.</p>
            """;

        var chave = RegrasEncerramentoAutomatico.ChaveAviso(prefixo, data, etapa);
        _db.TenantIdOverrideDeSistema = tenantId;
        try
        {
            foreach (var d in destinatarios)
                await _emailQueue.EnfileirarAsync(new EmailParaEnfileirar(
                    TipoEmailCatalogo.AvisoEncerramentoConta, d.Id, d.Email.Trim(), assunto, corpo, ChaveDeduplicacao: chave), cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
        return destinatarios.Count;
    }

    private Task<bool> TemOperadorAsync(Guid tenantId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.TenantId == tenantId && _plataforma.UsuariosOperadoresIds.Contains(u.Id), cancellationToken);
}
