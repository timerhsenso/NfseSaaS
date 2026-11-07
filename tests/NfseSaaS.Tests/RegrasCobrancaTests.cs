using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

public class RegrasCobrancaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 11, 10);

    private static SituacaoCobranca Avaliar(
        StatusAssinatura status,
        DateTimeOffset? testeIniciadoEm = null,
        DateOnly? primeiraCobrancaEm = null,
        DateOnly? atrasadaDesde = null,
        DateOnly? acessoAte = null,
        DateTimeOffset? agora = null,
        DateOnly? hoje = null) =>
        RegrasCobranca.Avaliar(status, testeIniciadoEm, primeiraCobrancaEm, atrasadaDesde, acessoAte, agora ?? Agora, hoje ?? Hoje);

    [Fact]
    public void Teste_que_nao_comecou_libera_sem_aviso()
    {
        var s = Avaliar(StatusAssinatura.Teste);
        Assert.True(s.PodeEmitirEmProducao);
        Assert.Null(s.Aviso);
    }

    [Fact]
    public void Teste_em_andamento_libera_e_avisa_so_os_dias()
    {
        var s = Avaliar(StatusAssinatura.Teste, Agora.AddDays(-10));
        Assert.True(s.PodeEmitirEmProducao);
        Assert.Contains("faltam 20 dia(s)", s.Aviso);
        Assert.DoesNotContain("nota", s.Aviso);
        Assert.Equal(Agora.AddDays(20), s.TesteTerminaEm);
    }

    [Fact]
    public void Teste_acaba_so_pelo_prazo()
    {
        Assert.Equal(30, Planos.TesteDias);
        var s = Avaliar(StatusAssinatura.Teste, Agora.AddDays(-Planos.TesteDias));
        Assert.False(s.PodeEmitirEmProducao);
        Assert.Contains("teste grátis de 30 dias terminou", s.Bloqueio);
        Assert.Contains("homologação", s.Bloqueio);
    }

    [Fact]
    public void Quem_assinou_no_teste_emite_ate_a_primeira_fatura_mais_a_tolerancia()
    {
        var inicio = new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);
        var primeira = new DateOnly(2026, 10, 31);

        var noTeste = Avaliar(StatusAssinatura.Teste, inicio, primeira, agora: inicio.AddDays(5), hoje: new DateOnly(2026, 10, 6));
        Assert.True(noTeste.PodeEmitirEmProducao);
        Assert.Contains("primeira fatura da assinatura vence em 31/10/2026", noTeste.Aviso);

        var noVencimento = Avaliar(StatusAssinatura.Teste, inicio, primeira, agora: inicio.AddDays(30).AddHours(1), hoje: primeira);
        Assert.True(noVencimento.PodeEmitirEmProducao);

        var dia10 = Avaliar(StatusAssinatura.Teste, inicio, primeira, agora: inicio.AddDays(40), hoje: primeira.AddDays(10));
        Assert.True(dia10.PodeEmitirEmProducao);
        Assert.Contains("primeira fatura da assinatura vencida em 31/10/2026", dia10.Aviso);
        Assert.Contains("para em 11/11/2026", dia10.Aviso);

        var dia11 = Avaliar(StatusAssinatura.Teste, inicio, primeira, agora: inicio.AddDays(41), hoje: primeira.AddDays(11));
        Assert.False(dia11.PodeEmitirEmProducao);
        Assert.Contains("não foi paga", dia11.Bloqueio);
    }

    [Fact]
    public void Sem_teste_gratis_bloqueia_na_hora_e_a_primeira_fatura_vence_hoje()
    {
        var s = RegrasCobranca.Avaliar(StatusAssinatura.Teste, Agora, null, null, null, Agora, Hoje, semTesteGratis: true);
        Assert.False(s.PodeEmitirEmProducao);
        Assert.Contains("já usou o teste grátis em outra conta", s.Bloqueio);

        // Assinou: emite até a primeira fatura (hoje) mais a tolerância, como quem já acabou o teste.
        var assinou = RegrasCobranca.Avaliar(StatusAssinatura.Teste, Agora, Hoje, null, null, Agora, Hoje, semTesteGratis: true);
        Assert.True(assinou.PodeEmitirEmProducao);

        var (venc, iniciar) = RegrasCobranca.PrimeiroVencimento(StatusAssinatura.Teste, null, null, Agora, Hoje, semTesteGratis: true);
        Assert.Equal(Hoje, venc);
        Assert.True(iniciar);
    }

    [Fact]
    public void Dias_extras_do_operador_estendem_o_teste_e_a_primeira_cobranca()
    {
        var inicio = Agora.AddDays(-35);
        Assert.False(RegrasCobranca.Avaliar(StatusAssinatura.Teste, inicio, null, null, null, Agora, Hoje).PodeEmitirEmProducao);

        var prorrogado = RegrasCobranca.Avaliar(StatusAssinatura.Teste, inicio, null, null, null, Agora, Hoje, diasExtrasTeste: 15);
        Assert.True(prorrogado.PodeEmitirEmProducao);
        Assert.Contains("faltam 10 dia(s)", prorrogado.Aviso);

        var (venc, _) = RegrasCobranca.PrimeiroVencimento(StatusAssinatura.Teste, inicio, null, Agora, Hoje, diasExtrasTeste: 15);
        Assert.Equal(Hoje.AddDays(10), venc);
        Assert.Equal(45, RegrasCobranca.DiasDeTeste(15));
    }

    [Fact]
    public void Isencao_vale_pela_configuracao_ou_pela_tela_ate_a_data_inclusive()
    {
        Assert.True(RegrasCobranca.Isenta(true, false, null, Hoje));
        Assert.True(RegrasCobranca.Isenta(false, true, null, Hoje));          // sem prazo
        Assert.True(RegrasCobranca.Isenta(false, true, Hoje, Hoje));          // último dia
        Assert.False(RegrasCobranca.Isenta(false, true, Hoje.AddDays(-1), Hoje));
        Assert.False(RegrasCobranca.Isenta(false, false, Hoje.AddDays(30), Hoje));
    }

    [Fact]
    public void Ativa_libera()
    {
        Assert.True(Avaliar(StatusAssinatura.Ativa, Agora.AddDays(-90)).PodeEmitirEmProducao);
    }

    [Fact]
    public void Atrasada_dentro_da_tolerancia_libera_com_aviso_e_depois_bloqueia()
    {
        var vencimento = Hoje.AddDays(-Planos.ToleranciaAtrasoDias);
        var dentro = Avaliar(StatusAssinatura.Atrasada, atrasadaDesde: vencimento);
        Assert.True(dentro.PodeEmitirEmProducao);
        Assert.Contains("ainda não foi paga", dentro.Aviso);

        var fora = Avaliar(StatusAssinatura.Atrasada, atrasadaDesde: vencimento.AddDays(-1));
        Assert.False(fora.PodeEmitirEmProducao);
        Assert.Contains("não foi paga", fora.Bloqueio);
    }

    [Fact]
    public void Tolerancia_de_atraso_e_de_10_dias_como_nos_termos()
    {
        // Termos de uso, item 8: suspensão só depois de 10 dias de atraso.
        Assert.Equal(10, Planos.ToleranciaAtrasoDias);

        var vencimento = new DateOnly(2026, 11, 1);
        var dia10 = Avaliar(StatusAssinatura.Atrasada, atrasadaDesde: vencimento, hoje: new DateOnly(2026, 11, 11));
        Assert.True(dia10.PodeEmitirEmProducao);
        Assert.Contains("para em 12/11/2026", dia10.Aviso);

        var dia11 = Avaliar(StatusAssinatura.Atrasada, atrasadaDesde: vencimento, hoje: new DateOnly(2026, 11, 12));
        Assert.False(dia11.PodeEmitirEmProducao);
    }

    [Fact]
    public void Cancelada_emite_ate_o_fim_do_periodo_pago_e_depois_bloqueia()
    {
        Assert.False(Avaliar(StatusAssinatura.Cancelada).PodeEmitirEmProducao);

        var ate = Hoje.AddDays(5);
        var dentro = Avaliar(StatusAssinatura.Cancelada, acessoAte: ate);
        Assert.True(dentro.PodeEmitirEmProducao);
        Assert.Contains("continua até 15/11/2026", dentro.Aviso);

        Assert.True(Avaliar(StatusAssinatura.Cancelada, acessoAte: Hoje).PodeEmitirEmProducao);
        Assert.False(Avaliar(StatusAssinatura.Cancelada, acessoAte: Hoje.AddDays(-1)).PodeEmitirEmProducao);
    }

    [Fact]
    public void Primeiro_vencimento_e_no_fim_do_teste_e_nunca_tira_dia_de_teste()
    {
        // Teste em andamento (começou há 10 dias às 12h UTC = 9h de Brasília): vence no dia em que o teste acaba.
        var (venc, iniciar) = RegrasCobranca.PrimeiroVencimento(StatusAssinatura.Teste, Agora.AddDays(-10), null, Agora, Hoje);
        Assert.Equal(new DateOnly(2026, 11, 30), venc);
        Assert.False(iniciar);

        // Teste que não começou: começa ao assinar, e a fatura vence em 30 dias.
        (venc, iniciar) = RegrasCobranca.PrimeiroVencimento(StatusAssinatura.Teste, null, null, Agora, Hoje);
        Assert.Equal(new DateOnly(2026, 12, 10), venc);
        Assert.True(iniciar);

        // Teste acabado: vence hoje.
        (venc, _) = RegrasCobranca.PrimeiroVencimento(StatusAssinatura.Teste, Agora.AddDays(-40), null, Agora, Hoje);
        Assert.Equal(Hoje, venc);

        // Cancelada com dias pagos: vence no dia seguinte ao último dia pago.
        (venc, _) = RegrasCobranca.PrimeiroVencimento(StatusAssinatura.Cancelada, Agora.AddDays(-90), Hoje.AddDays(12), Agora, Hoje);
        Assert.Equal(Hoje.AddDays(13), venc);

        // Cancelada sem dias pagos: vence hoje.
        (venc, _) = RegrasCobranca.PrimeiroVencimento(StatusAssinatura.Cancelada, Agora.AddDays(-90), null, Agora, Hoje);
        Assert.Equal(Hoje, venc);
    }
}
