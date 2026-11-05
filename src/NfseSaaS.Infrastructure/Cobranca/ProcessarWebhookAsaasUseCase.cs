using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
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
/// - PAYMENT_OVERDUE: Atrasada desde o vencimento (tolerância em RegrasCobranca).
/// - PAYMENT_CREATED / PAYMENT_UPDATED: guarda o link da fatura em aberto.
/// - SUBSCRIPTION_DELETED / SUBSCRIPTION_INACTIVATED: Cancelada.
/// </summary>
public sealed class ProcessarWebhookAsaasUseCase : IProcessarWebhookAsaasUseCase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProcessarWebhookAsaasUseCase> _logger;

    public ProcessarWebhookAsaasUseCase(AppDbContext db, ILogger<ProcessarWebhookAsaasUseCase> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> ExecutarAsync(string payload, CancellationToken cancellationToken)
    {
        string? eventoId, tipo, pagamentoId = null, assinaturaId = null, faturaUrl = null, statusPagamento = null;
        DateOnly? vencimento = null;
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
        evento.Resultado = assinaturaLocal is null
            ? "ignorado: assinatura não encontrada"
            : Aplicar(assinaturaLocal, tipo, vencimento, faturaUrl, statusPagamento);
        evento.Processado = assinaturaLocal is not null;

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

        _logger.LogInformation("Webhook Asaas {Tipo} ({EventoId}) da assinatura {AssinaturaId}: {Resultado}",
            tipo, eventoId, assinaturaId, evento.Resultado);
        return evento.Resultado;
    }

    private static string Aplicar(Assinatura assinatura, string tipo, DateOnly? vencimento, string? faturaUrl, string? statusPagamento)
    {
        switch (tipo)
        {
            case "PAYMENT_CONFIRMED":
            case "PAYMENT_RECEIVED":
                assinatura.Status = StatusAssinatura.Ativa;
                assinatura.AtrasadaDesde = null;
                assinatura.CanceladaEm = null;
                assinatura.UltimoPagamentoEm = DateTimeOffset.UtcNow;
                assinatura.UrlFaturaPendente = null;
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
                    assinatura.UrlFaturaPendente = Limitar(faturaUrl, 300);
                return "fatura vencida";

            case "PAYMENT_CREATED":
            case "PAYMENT_UPDATED":
                if (statusPagamento is "PENDING" or "OVERDUE" && !string.IsNullOrWhiteSpace(faturaUrl))
                {
                    assinatura.UrlFaturaPendente = Limitar(faturaUrl, 300);
                    return "fatura em aberto registrada";
                }
                return "sem mudança";

            case "SUBSCRIPTION_DELETED":
            case "SUBSCRIPTION_INACTIVATED":
                assinatura.Status = StatusAssinatura.Cancelada;
                assinatura.CanceladaEm ??= DateTimeOffset.UtcNow;
                assinatura.UrlFaturaPendente = null;
                return "assinatura cancelada";

            default:
                return "sem mudança";
        }
    }

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static string? Limitar(string? texto, int maximo) =>
        texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];
}
