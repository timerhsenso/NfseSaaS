using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Webhook do Asaas. Fluxo recomendado pela documentação: grava o evento
/// (id único = idempotência, a entrega é "pelo menos uma vez"), aplica a
/// regra e responde 200 rápido. Evento repetido não aplica nada de novo;
/// evento desconhecido ou de assinatura que não é nossa fica gravado como
/// ignorado (responder erro pausaria a fila do Asaas depois de 15 falhas).
///
/// Regras:
/// - PAYMENT_CONFIRMED / PAYMENT_RECEIVED: assinatura Ativa, sai do atraso.
///   Se for o PRIMEIRO pagamento de uma conta indicada, registra os prêmios
///   do "Indique e ganhe" (na mesma gravação do evento) e dispara a
///   aplicação no Asaas por job (nunca dentro do webhook).
/// - PAYMENT_OVERDUE: Atrasada desde o vencimento (tolerância em RegrasCobranca).
/// - PAYMENT_CREATED / PAYMENT_UPDATED / PAYMENT_RESTORED: guarda o link da fatura em aberto.
/// - PAYMENT_DELETED: esquece o link se era o da fatura removida (ex.: mês grátis).
/// - SUBSCRIPTION_DELETED / SUBSCRIPTION_INACTIVATED: Cancelada.
/// - PAYMENT_REFUNDED / PAYMENT_REFUND_IN_PROGRESS / PAYMENT_CHARGEBACK_REQUESTED /
///   PAYMENT_CHARGEBACK_DISPUTE / PAYMENT_RECEIVED_IN_CASH_UNDONE: o pagamento foi
///   desfeito: Atrasada desde hoje (a tolerância conta de quando soubemos).
/// - PAYMENT_AWAITING_CHARGEBACK_REVERSAL (contestação ganha) /
///   PAYMENT_DUNNING_RECEIVED (negativação paga): volta a Ativa.
/// A conciliação diária (ConciliacaoCobrancaService) confere tudo isso com o
/// estado real do Asaas e corrige o que um webhook perdido deixou para trás.
/// Depois de aplicar, se a cobrança vale para a conta, agenda os avisos de
/// cobrança dela (IAvisarCobrancaJob) por job, nunca dentro do webhook.
/// </summary>
public sealed class ProcessarWebhookAsaasUseCase : IProcessarWebhookAsaasUseCase
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly CobrancaOptions _cobranca;
    private readonly ILogger<ProcessarWebhookAsaasUseCase> _logger;

    public ProcessarWebhookAsaasUseCase(
        AppDbContext db,
        IBackgroundJobClient jobs,
        IOptions<CobrancaOptions> cobranca,
        ILogger<ProcessarWebhookAsaasUseCase> logger)
    {
        _db = db;
        _jobs = jobs;
        _cobranca = cobranca.Value;
        _logger = logger;
    }

    public async Task<string> ExecutarAsync(string payload, CancellationToken cancellationToken)
    {
        string? eventoId, tipo, pagamentoId = null, assinaturaId = null, faturaUrl = null, statusPagamento = null;
        DateOnly? vencimento = null;
        decimal? valor = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var raiz = doc.RootElement;
            eventoId = Texto(raiz, "id");
            tipo = Texto(raiz, "event");

            if (raiz.TryGetProperty("payment", out var pagamento) && pagamento.ValueKind == JsonValueKind.Object)
            {
                pagamentoId = Texto(pagamento, "id");
                assinaturaId = Texto(pagamento, "subscription");
                faturaUrl = Texto(pagamento, "invoiceUrl");
                statusPagamento = Texto(pagamento, "status");
                if (DateOnly.TryParseExact(Texto(pagamento, "dueDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    vencimento = d;
                if (pagamento.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var dec))
                    valor = decimal.Round(dec, 2);
            }
            else if (raiz.TryGetProperty("subscription", out var assinatura) && assinatura.ValueKind == JsonValueKind.Object)
            {
                assinaturaId = Texto(assinatura, "id");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Webhook do Asaas com JSON inválido; ignorado.");
            return "ignorado: JSON inválido";
        }

        if (string.IsNullOrWhiteSpace(eventoId) || string.IsNullOrWhiteSpace(tipo))
            return "ignorado: sem id ou tipo";

        if (await _db.EventosCobranca.AsNoTracking().AnyAsync(e => e.EventoId == eventoId, cancellationToken))
            return "repetido";

        var evento = new EventoCobranca
        {
            EventoId = Limitar(eventoId, 200)!,
            Tipo = Limitar(tipo, 60)!,
            AsaasPagamentoId = Limitar(pagamentoId, 60),
            AsaasAssinaturaId = Limitar(assinaturaId, 60),
            Payload = payload
        };

        var assinaturaLocal = assinaturaId is null
            ? null
            : await _db.Assinaturas.FirstOrDefaultAsync(a => a.AsaasAssinaturaId == assinaturaId, cancellationToken);

        evento.TenantId = assinaturaLocal?.TenantId;
        evento.Processado = assinaturaLocal is not null;
        var dispararPremios = false;

        if (assinaturaLocal is null)
        {
            evento.Resultado = "ignorado: assinatura não encontrada";
        }
        else
        {
            var primeiroPagamento = assinaturaLocal.UltimoPagamentoEm is null;
            evento.Resultado = Aplicar(assinaturaLocal, tipo, vencimento, valor, faturaUrl, statusPagamento);

            if (tipo is "PAYMENT_CONFIRMED" or "PAYMENT_RECEIVED")
            {
                var registrou = primeiroPagamento && await RegistrarPremiosIndicacaoAsync(assinaturaLocal.TenantId, pagamentoId, cancellationToken);
                if (registrou)
                    evento.Resultado += "; prêmio de indicação registrado";

                // Quem INDICOU também pode ter prêmio guardado esperando este pagamento.
                dispararPremios = registrou || await _db.PremiosIndicacao.AsNoTracking().AnyAsync(
                    p => p.TenantId == assinaturaLocal.TenantId && p.Status == StatusPremioIndicacao.Pendente, cancellationToken);
            }
        }

        _db.EventosCobranca.Add(evento);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // O mesmo evento chegou duas vezes ao mesmo tempo: o outro já gravou.
            if (await _db.EventosCobranca.AsNoTracking().AnyAsync(e => e.EventoId == eventoId, CancellationToken.None))
                return "repetido";
            throw;
        }

        if (evento.Resultado.StartsWith("pagamento desfeito", StringComparison.Ordinal))
            _logger.LogWarning("Webhook Asaas {Tipo} ({EventoId}): pagamento {PagamentoId} da conta {TenantId} desfeito — conferir no painel do Asaas.",
                tipo, eventoId, pagamentoId, assinaturaLocal?.TenantId);
        else
            _logger.LogInformation("Webhook Asaas {Tipo} ({EventoId}) da assinatura {AssinaturaId}: {Resultado}",
                tipo, eventoId, assinaturaId, evento.Resultado);

        if (assinaturaLocal is not null)
            AgendarAvisosCobranca(assinaturaLocal.TenantId);

        if (dispararPremios)
        {
            try
            {
                _jobs.Enqueue<IAplicarPremiosIndicacaoJob>(job => job.ExecutarAsync());
            }
            catch (Exception ex)
            {
                // O evento já está gravado; a varredura diária aplica o prêmio.
                _logger.LogWarning(ex, "Não foi possível agendar a aplicação do prêmio de indicação; fica para a varredura diária.");
            }
        }

        return evento.Resultado;
    }

    /// <summary>
    /// Avisos de cobrança da conta logo após o evento (pagamento confirmado,
    /// fatura criada, vencida, cancelamento). Só quando a cobrança vale para
    /// a conta; a falha em agendar não afeta o webhook (o job diário cobre).
    /// </summary>
    private void AgendarAvisosCobranca(Guid tenantId)
    {
        if (!_cobranca.AtivaPara(tenantId) || _cobranca.ContasIsentas.Contains(tenantId))
            return;

        try
        {
            _jobs.Enqueue<IAvisarCobrancaJob>(job => job.ExecutarContaAsync(tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível agendar os avisos de cobrança da conta {TenantId}; ficam para o job diário.", tenantId);
        }
    }

    /// <summary>
    /// Primeiro pagamento de uma conta indicada: um prêmio para ela e um para
    /// quem indicou. O índice único (IndicadoTenantId, Papel) segura a
    /// repetição; a consulta antes evita depender do erro de gravação.
    /// </summary>
    private async Task<bool> RegistrarPremiosIndicacaoAsync(Guid tenantId, string? pagamentoId, CancellationToken cancellationToken)
    {
        var indicadoPor = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.IndicadoPorTenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!RegrasPremioIndicacao.GeraPremio(primeiroPagamento: true, tenantId, indicadoPor))
            return false;

        if (await _db.PremiosIndicacao.AsNoTracking().AnyAsync(p => p.IndicadoTenantId == tenantId, cancellationToken))
            return false;

        var origem = Limitar(pagamentoId, 60);
        _db.PremiosIndicacao.Add(new PremioIndicacao
        {
            TenantId = tenantId, IndicadoTenantId = tenantId, Papel = PapelPremioIndicacao.ContaIndicada,
            AsaasPagamentoOrigemId = origem, Detalhe = "Aguardando aplicação."
        });
        _db.PremiosIndicacao.Add(new PremioIndicacao
        {
            TenantId = indicadoPor!.Value, IndicadoTenantId = tenantId, Papel = PapelPremioIndicacao.QuemIndicou,
            AsaasPagamentoOrigemId = origem, Detalhe = "Aguardando aplicação."
        });
        return true;
    }

    private static string Aplicar(Assinatura assinatura, string tipo, DateOnly? vencimento, decimal? valor, string? faturaUrl, string? statusPagamento)
    {
        switch (tipo)
        {
            case "PAYMENT_CONFIRMED":
            case "PAYMENT_RECEIVED":
                assinatura.Status = StatusAssinatura.Ativa;
                assinatura.AtrasadaDesde = null;
                assinatura.CanceladaEm = null;
                assinatura.UltimoPagamentoEm = DateTimeOffset.UtcNow;
                assinatura.LimparFaturaPendente();
                return "assinatura ativa";

            case "PAYMENT_OVERDUE":
                if (assinatura.Status == StatusAssinatura.Cancelada)
                    return "ignorado: assinatura cancelada";
                // Primeira fatura vencida sem nunca ter pago: continua em "Teste"
                // (o teste grátis decide); depois de ativa, vira "Atrasada".
                if (assinatura.Status == StatusAssinatura.Ativa || assinatura.Status == StatusAssinatura.Atrasada)
                {
                    assinatura.Status = StatusAssinatura.Atrasada;
                    var desde = vencimento ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
                    assinatura.AtrasadaDesde = assinatura.AtrasadaDesde is { } atual && atual < desde ? atual : desde;
                }
                if (!string.IsNullOrWhiteSpace(faturaUrl))
                    GuardarFaturaPendente(assinatura, faturaUrl, vencimento, valor);
                return "fatura vencida";

            case "PAYMENT_CREATED":
            case "PAYMENT_UPDATED":
            case "PAYMENT_RESTORED":
                if (statusPagamento is "PENDING" or "OVERDUE" && !string.IsNullOrWhiteSpace(faturaUrl))
                {
                    GuardarFaturaPendente(assinatura, faturaUrl, vencimento, valor);
                    return "fatura em aberto registrada";
                }
                return "sem mudança";

            case "PAYMENT_DELETED":
                if (!string.IsNullOrWhiteSpace(faturaUrl) && assinatura.UrlFaturaPendente == Limitar(faturaUrl, 300))
                {
                    assinatura.LimparFaturaPendente();
                    return "fatura removida";
                }
                return "sem mudança";

            case "PAYMENT_REFUNDED":
            case "PAYMENT_REFUND_IN_PROGRESS":
            case "PAYMENT_CHARGEBACK_REQUESTED":
            case "PAYMENT_CHARGEBACK_DISPUTE":
            case "PAYMENT_RECEIVED_IN_CASH_UNDONE":
            {
                if (assinatura.Status == StatusAssinatura.Cancelada)
                    return "ignorado: assinatura cancelada";
                var hoje = DataBrasilia.Hoje();
                var desde = assinatura.Status == StatusAssinatura.Atrasada && assinatura.AtrasadaDesde is { } atual && atual < hoje ? atual : hoje;
                assinatura.Status = StatusAssinatura.Atrasada;
                assinatura.AtrasadaDesde = desde;
                return "pagamento desfeito (estorno ou contestação): atrasada";
            }

            case "PAYMENT_AWAITING_CHARGEBACK_REVERSAL":
            case "PAYMENT_DUNNING_RECEIVED":
                if (assinatura.Status == StatusAssinatura.Cancelada)
                    return "ignorado: assinatura cancelada";
                assinatura.Status = StatusAssinatura.Ativa;
                assinatura.AtrasadaDesde = null;
                assinatura.UltimoPagamentoEm ??= DateTimeOffset.UtcNow;
                return "pagamento restabelecido: assinatura ativa";

            case "SUBSCRIPTION_DELETED":
            case "SUBSCRIPTION_INACTIVATED":
                assinatura.Status = StatusAssinatura.Cancelada;
                assinatura.CanceladaEm ??= DateTimeOffset.UtcNow;
                assinatura.LimparFaturaPendente();
                return "assinatura cancelada";

            default:
                return "sem mudança";
        }
    }

    private static void GuardarFaturaPendente(Assinatura assinatura, string faturaUrl, DateOnly? vencimento, decimal? valor)
    {
        assinatura.UrlFaturaPendente = Limitar(faturaUrl, 300);
        assinatura.VencimentoFaturaPendente = vencimento;
        assinatura.ValorFaturaPendente = valor;
    }

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static string? Limitar(string? texto, int maximo) =>
        texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];
}
