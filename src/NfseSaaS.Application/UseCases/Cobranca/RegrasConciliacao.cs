using System.Globalization;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Uma fatura (cobrança) da assinatura como está no gateway agora.</summary>
/// <param name="Status">Status do Asaas (PENDING, RECEIVED, OVERDUE, REFUNDED...).</param>
/// <param name="PagaEm">Dia do pagamento, quando paga.</param>
public sealed record FaturaConciliacao(string Id, string Status, DateOnly? Vencimento, DateOnly? PagaEm, string? Url, decimal? Valor);

/// <summary>A assinatura no gateway: encerrada (removida, inativa ou expirada) e as faturas dela.</summary>
public sealed record AssinaturaGatewayConciliacao(bool Encerrada, IReadOnlyList<FaturaConciliacao> Faturas);

/// <summary>O que o sistema guarda hoje (antes de conciliar).</summary>
public sealed record EstadoLocalConciliacao(
    StatusAssinatura Status,
    DateOnly? AtrasadaDesde,
    DateTimeOffset? UltimoPagamentoEm,
    string? UrlFaturaPendente,
    DateOnly? VencimentoFaturaPendente,
    decimal? ValorFaturaPendente);

/// <summary>Como a assinatura deve ficar, e o que mudou (vazio = já estava certa).</summary>
/// <param name="PrimeiroPagamentoEm">Preenchido quando o sistema não sabia de pagamento nenhum e o gateway mostra um.</param>
/// <param name="PrimeiroPagamentoId">
/// A fatura paga desse primeiro pagamento (junto com <paramref name="PrimeiroPagamentoEm"/>):
/// é a origem do prêmio de indicação que o webhook perdido deixaria de registrar.
/// </param>
public sealed record ResultadoConciliacao(
    StatusAssinatura Status,
    DateOnly? AtrasadaDesde,
    DateOnly? PrimeiroPagamentoEm,
    FaturaConciliacao? FaturaPendente,
    IReadOnlyList<string> Mudancas,
    string? PrimeiroPagamentoId = null)
{
    public bool Mudou => Mudancas.Count > 0;
}

/// <summary>
/// Conciliação com o gateway (L5), sem banco nem HTTP (testável isolada).
/// O webhook pode se perder (o Asaas pausa a fila depois de falhas; a VPS
/// pode estar fora no momento), então uma vez por dia o sistema lê o estado
/// real de cada assinatura e corrige o seu:
/// - assinatura encerrada no gateway → Cancelada (local Cancelada nunca é
///   reativada por aqui: cancelar é decisão da conta ou do operador);
/// - o pagamento mais recente resolve as pendências de faturas anteriores
///   (igual ao webhook: qualquer pagamento tira a conta do atraso);
/// - depois dele, fatura vencida → Atrasada desde o vencimento; fatura
///   estornada ou contestada (chargeback) → Atrasada desde o dia em que o
///   sistema soube (a tolerância de 10 dias conta dali);
/// - quem nunca pagou nada continua em Teste: o fim do teste e a primeira
///   fatura decidem (RegrasCobranca);
/// - o primeiro pagamento que o sistema não conhecia sai com a fatura de
///   origem (PrimeiroPagamentoId), para registrar o prêmio de indicação;
/// - o link da fatura em aberto é sempre o da mais antiga não paga.
/// </summary>
public static class RegrasConciliacao
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Paga (inclusive negativação recebida e chargeback ganho, aguardando o repasse).</summary>
    public static readonly IReadOnlySet<string> StatusPagos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "RECEIVED", "CONFIRMED", "RECEIVED_IN_CASH", "DUNNING_RECEIVED", "AWAITING_CHARGEBACK_REVERSAL" };

    /// <summary>Foi paga e o pagamento foi desfeito ou está sendo contestado.</summary>
    public static readonly IReadOnlySet<string> StatusDesfeitos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "REFUNDED", "REFUND_REQUESTED", "REFUND_IN_PROGRESS", "CHARGEBACK_REQUESTED", "CHARGEBACK_DISPUTE" };

    /// <summary>Venceu sem pagamento.</summary>
    public static readonly IReadOnlySet<string> StatusVencidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "OVERDUE", "DUNNING_REQUESTED" };

    /// <summary>Em aberto, com link para pagar.</summary>
    public static readonly IReadOnlySet<string> StatusEmAberto = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "PENDING", "OVERDUE", "DUNNING_REQUESTED" };

    public static ResultadoConciliacao Avaliar(EstadoLocalConciliacao local, AssinaturaGatewayConciliacao gateway, DateOnly hoje)
    {
        var mudancas = new List<string>();

        if (local.Status == StatusAssinatura.Cancelada)
            return new ResultadoConciliacao(local.Status, local.AtrasadaDesde, null, FaturaAtual(local), mudancas);

        if (gateway.Encerrada)
        {
            mudancas.Add("assinatura encerrada no Asaas: Cancelada");
            if (local.UrlFaturaPendente is not null)
                mudancas.Add("link da fatura em aberto removido");
            return new ResultadoConciliacao(StatusAssinatura.Cancelada, null, null, null, mudancas);
        }

        var pagas = gateway.Faturas.Where(f => StatusPagos.Contains(f.Status)).ToList();
        var ultimaPagaVence = pagas.Select(f => f.Vencimento).Where(v => v is not null).Max();

        bool DepoisDaUltimaPaga(FaturaConciliacao f) =>
            ultimaPagaVence is not { } limite || f.Vencimento is not { } v || v > limite;

        var vencidas = gateway.Faturas.Where(f => StatusVencidos.Contains(f.Status) && DepoisDaUltimaPaga(f)).ToList();
        var desfeitas = gateway.Faturas.Where(f => StatusDesfeitos.Contains(f.Status) && DepoisDaUltimaPaga(f)).ToList();

        var jaPagou = pagas.Count > 0
            || gateway.Faturas.Any(f => StatusDesfeitos.Contains(f.Status))
            || local.UltimoPagamentoEm is not null;

        var status = local.Status;
        var atrasadaDesde = local.AtrasadaDesde;

        if ((vencidas.Count > 0 || desfeitas.Count > 0) && jaPagou)
        {
            DateOnly? desde = null;
            if (vencidas.Select(f => f.Vencimento).Where(v => v is not null).Min() is { } primeiraVencida)
                desde = primeiraVencida;
            if (desfeitas.Count > 0)
            {
                // Estorno/chargeback: a tolerância conta de quando o sistema soube.
                var soube = local.Status == StatusAssinatura.Atrasada && local.AtrasadaDesde is { } ja ? ja : hoje;
                desde = desde is { } d && d < soube ? d : soube;
            }
            desde ??= local.AtrasadaDesde ?? hoje;
            if (local.Status == StatusAssinatura.Atrasada && local.AtrasadaDesde is { } atual && atual < desde)
                desde = atual;

            status = StatusAssinatura.Atrasada;
            atrasadaDesde = desde;
            if (local.Status != StatusAssinatura.Atrasada || local.AtrasadaDesde != desde)
                mudancas.Add(desfeitas.Count > 0
                    ? $"pagamento estornado ou contestado: Atrasada desde {Data(desde.Value)}"
                    : $"fatura vencida em {Data(desde.Value)} sem pagamento: Atrasada");
        }
        else if (pagas.Count > 0 && vencidas.Count == 0 && desfeitas.Count == 0)
        {
            status = StatusAssinatura.Ativa;
            atrasadaDesde = null;
            if (local.Status != StatusAssinatura.Ativa || local.AtrasadaDesde is not null)
                mudancas.Add("pagamento confirmado no Asaas: Ativa");
        }

        DateOnly? primeiroPagamento = null;
        string? primeiroPagamentoId = null;
        if (local.UltimoPagamentoEm is null && pagas.Count > 0)
        {
            // A paga mais recente (sem data vai para o fim): dia e id vêm da mesma fatura.
            var paga = pagas.OrderByDescending(f => f.PagaEm ?? f.Vencimento).First();
            primeiroPagamento = paga.PagaEm ?? paga.Vencimento ?? hoje;
            primeiroPagamentoId = string.IsNullOrWhiteSpace(paga.Id) ? null : paga.Id;
            mudancas.Add($"pagamento de {Data(primeiroPagamento.Value)} registrado");
        }

        var fatura = gateway.Faturas
            .Where(f => StatusEmAberto.Contains(f.Status) && !string.IsNullOrWhiteSpace(f.Url) && f.Vencimento is not null)
            .OrderBy(f => f.Vencimento)
            .FirstOrDefault();

        if (fatura is null && local.UrlFaturaPendente is not null)
            mudancas.Add("link da fatura em aberto removido (nenhuma em aberto no Asaas)");
        else if (fatura is not null
                 && (fatura.Url != local.UrlFaturaPendente || fatura.Vencimento != local.VencimentoFaturaPendente || fatura.Valor != local.ValorFaturaPendente))
            mudancas.Add($"fatura em aberto atualizada (vence em {Data(fatura.Vencimento!.Value)})");

        return new ResultadoConciliacao(status, atrasadaDesde, primeiroPagamento, fatura, mudancas, primeiroPagamentoId);
    }

    private static FaturaConciliacao? FaturaAtual(EstadoLocalConciliacao local) =>
        local.UrlFaturaPendente is null
            ? null
            : new FaturaConciliacao("", "PENDING", local.VencimentoFaturaPendente, null, local.UrlFaturaPendente, local.ValorFaturaPendente);

    private static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", PtBr);
}
