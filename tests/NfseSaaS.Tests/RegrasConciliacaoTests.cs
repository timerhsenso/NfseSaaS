using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Conciliação diária com o Asaas: o estado real do gateway corrige o sistema.</summary>
public class RegrasConciliacaoTests
{
    private static readonly DateOnly Hoje = new(2026, 12, 15);

    private static EstadoLocalConciliacao Local(
        StatusAssinatura status,
        DateOnly? atrasadaDesde = null,
        DateTimeOffset? ultimoPagamento = null,
        string? url = null,
        DateOnly? vencimento = null,
        decimal? valor = null) =>
        new(status, atrasadaDesde, ultimoPagamento, url, vencimento, valor);

    private static FaturaConciliacao Fatura(string status, int mes, int dia = 10, string? url = null, DateOnly? pagaEm = null) =>
        new($"pay_{mes}_{status}", status, new DateOnly(2026, mes, dia), pagaEm, url, 49.90m);

    private static AssinaturaGatewayConciliacao Gateway(params FaturaConciliacao[] faturas) => new(false, faturas);

    [Fact]
    public void Webhook_de_pagamento_perdido_ativa_e_registra_o_pagamento()
    {
        var r = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Teste, url: "https://asaas/i/11", vencimento: new DateOnly(2026, 11, 10), valor: 49.90m),
            Gateway(Fatura("RECEIVED", 11, pagaEm: new DateOnly(2026, 11, 9)), Fatura("PENDING", 12, 20, "https://asaas/i/12")),
            Hoje);

        Assert.True(r.Mudou);
        Assert.Equal(StatusAssinatura.Ativa, r.Status);
        Assert.Null(r.AtrasadaDesde);
        Assert.Equal(new DateOnly(2026, 11, 9), r.PrimeiroPagamentoEm);
        Assert.Equal("https://asaas/i/12", r.FaturaPendente!.Url);
        Assert.Contains("pagamento confirmado no Asaas: Ativa", r.Mudancas);
    }

    [Fact]
    public void Estado_igual_ao_asaas_nao_muda_nada()
    {
        var r = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Ativa, ultimoPagamento: DateTimeOffset.UtcNow, url: "https://asaas/i/12", vencimento: new DateOnly(2026, 12, 20), valor: 49.90m),
            Gateway(Fatura("RECEIVED", 11), Fatura("PENDING", 12, 20, "https://asaas/i/12")),
            Hoje);

        Assert.False(r.Mudou);
        Assert.Equal(StatusAssinatura.Ativa, r.Status);
    }

    [Fact]
    public void Fatura_vencida_depois_do_ultimo_pagamento_marca_atraso_desde_o_vencimento()
    {
        var r = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Ativa, ultimoPagamento: DateTimeOffset.UtcNow),
            Gateway(Fatura("RECEIVED", 11), Fatura("OVERDUE", 12, 10, "https://asaas/i/12")),
            Hoje);

        Assert.Equal(StatusAssinatura.Atrasada, r.Status);
        Assert.Equal(new DateOnly(2026, 12, 10), r.AtrasadaDesde);
        Assert.Equal("https://asaas/i/12", r.FaturaPendente!.Url);
    }

    [Fact]
    public void Pagamento_mais_recente_resolve_pendencia_antiga()
    {
        var r = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Atrasada, atrasadaDesde: new DateOnly(2026, 10, 10), ultimoPagamento: DateTimeOffset.UtcNow),
            Gateway(Fatura("OVERDUE", 10), Fatura("REFUNDED", 9), Fatura("RECEIVED", 11)),
            Hoje);

        Assert.Equal(StatusAssinatura.Ativa, r.Status);
        Assert.Null(r.AtrasadaDesde);
    }

    [Fact]
    public void Estorno_ou_chargeback_marca_atraso_desde_hoje_e_mantem_data_mais_antiga()
    {
        var estorno = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Ativa, ultimoPagamento: DateTimeOffset.UtcNow),
            Gateway(Fatura("RECEIVED", 10), Fatura("CHARGEBACK_REQUESTED", 11)),
            Hoje);
        Assert.Equal(StatusAssinatura.Atrasada, estorno.Status);
        Assert.Equal(Hoje, estorno.AtrasadaDesde);
        Assert.Contains("estornado ou contestado", estorno.Mudancas[0]);

        // Já estava atrasada (o webhook avisou há 3 dias): não reinicia a tolerância.
        var jaSabia = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Atrasada, atrasadaDesde: Hoje.AddDays(-3), ultimoPagamento: DateTimeOffset.UtcNow),
            Gateway(Fatura("RECEIVED", 10), Fatura("CHARGEBACK_DISPUTE", 11)),
            Hoje);
        Assert.False(jaSabia.Mudou);
        Assert.Equal(Hoje.AddDays(-3), jaSabia.AtrasadaDesde);

        // Primeiro (e único) pagamento estornado: conta como quem já pagou.
        var unico = RegrasConciliacao.Avaliar(Local(StatusAssinatura.Ativa), Gateway(Fatura("REFUNDED", 11)), Hoje);
        Assert.Equal(StatusAssinatura.Atrasada, unico.Status);
    }

    [Fact]
    public void Chargeback_ganho_conta_como_pago()
    {
        var r = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Atrasada, atrasadaDesde: Hoje.AddDays(-5), ultimoPagamento: DateTimeOffset.UtcNow),
            Gateway(Fatura("AWAITING_CHARGEBACK_REVERSAL", 11)),
            Hoje);
        Assert.Equal(StatusAssinatura.Ativa, r.Status);
        Assert.Null(r.AtrasadaDesde);
    }

    [Fact]
    public void Quem_nunca_pagou_continua_em_teste_mesmo_com_fatura_vencida()
    {
        var r = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Teste),
            Gateway(Fatura("OVERDUE", 12, 1, "https://asaas/i/12")),
            Hoje);

        Assert.Equal(StatusAssinatura.Teste, r.Status);
        Assert.Null(r.AtrasadaDesde);
        Assert.Equal("https://asaas/i/12", r.FaturaPendente!.Url);
        Assert.Contains(r.Mudancas, m => m.StartsWith("fatura em aberto atualizada"));
    }

    [Fact]
    public void Assinatura_encerrada_no_asaas_cancela_e_cancelada_nunca_reativa()
    {
        var encerrada = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Ativa, url: "https://asaas/i/12"),
            new AssinaturaGatewayConciliacao(true, [Fatura("RECEIVED", 11)]),
            Hoje);
        Assert.Equal(StatusAssinatura.Cancelada, encerrada.Status);
        Assert.Null(encerrada.FaturaPendente);

        var cancelada = RegrasConciliacao.Avaliar(Local(StatusAssinatura.Cancelada), Gateway(Fatura("RECEIVED", 12)), Hoje);
        Assert.False(cancelada.Mudou);
        Assert.Equal(StatusAssinatura.Cancelada, cancelada.Status);
    }

    [Fact]
    public void Sem_fatura_em_aberto_no_asaas_o_link_sai()
    {
        var r = RegrasConciliacao.Avaliar(
            Local(StatusAssinatura.Ativa, ultimoPagamento: DateTimeOffset.UtcNow, url: "https://asaas/i/velho", vencimento: new DateOnly(2026, 11, 10)),
            Gateway(Fatura("RECEIVED", 11)),
            Hoje);
        Assert.Null(r.FaturaPendente);
        Assert.Contains(r.Mudancas, m => m.StartsWith("link da fatura em aberto removido"));
    }
}
