using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

public class RegrasCobrancaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 11, 10);

    [Fact]
    public void Teste_que_nao_comecou_libera_sem_aviso()
    {
        var s = RegrasCobranca.Avaliar(StatusAssinatura.Teste, null, 0, null, Agora, Hoje);
        Assert.True(s.PodeEmitirEmProducao);
        Assert.Null(s.Aviso);
    }

    [Fact]
    public void Teste_em_andamento_libera_e_avisa_o_que_falta()
    {
        var s = RegrasCobranca.Avaliar(StatusAssinatura.Teste, Agora.AddDays(-10), 3, null, Agora, Hoje);
        Assert.True(s.PodeEmitirEmProducao);
        Assert.Contains("20 dia(s) e 7 nota(s)", s.Aviso);
        Assert.Equal(Agora.AddDays(20), s.TesteTerminaEm);
    }

    [Fact]
    public void Teste_acaba_pelo_prazo()
    {
        var s = RegrasCobranca.Avaliar(StatusAssinatura.Teste, Agora.AddDays(-Planos.TesteDias), 0, null, Agora, Hoje);
        Assert.False(s.PodeEmitirEmProducao);
        Assert.Contains("teste grátis terminou", s.Bloqueio);
        Assert.Contains("homologação", s.Bloqueio);
    }

    [Fact]
    public void Teste_acaba_pelas_notas_antes_do_prazo()
    {
        var s = RegrasCobranca.Avaliar(StatusAssinatura.Teste, Agora.AddDays(-2), Planos.TesteNotas, null, Agora, Hoje);
        Assert.False(s.PodeEmitirEmProducao);
    }

    [Fact]
    public void Ativa_libera()
    {
        Assert.True(RegrasCobranca.Avaliar(StatusAssinatura.Ativa, Agora.AddDays(-90), 500, null, Agora, Hoje).PodeEmitirEmProducao);
    }

    [Fact]
    public void Atrasada_dentro_da_tolerancia_libera_com_aviso_e_depois_bloqueia()
    {
        var vencimento = Hoje.AddDays(-Planos.ToleranciaAtrasoDias);
        var dentro = RegrasCobranca.Avaliar(StatusAssinatura.Atrasada, null, 0, vencimento, Agora, Hoje);
        Assert.True(dentro.PodeEmitirEmProducao);
        Assert.Contains("ainda não foi paga", dentro.Aviso);

        var fora = RegrasCobranca.Avaliar(StatusAssinatura.Atrasada, null, 0, vencimento.AddDays(-1), Agora, Hoje);
        Assert.False(fora.PodeEmitirEmProducao);
        Assert.Contains("não foi paga", fora.Bloqueio);
    }

    [Fact]
    public void Tolerancia_de_atraso_e_de_10_dias_como_nos_termos()
    {
        // Termos de uso, item 8: suspensão só depois de 10 dias de atraso.
        Assert.Equal(10, Planos.ToleranciaAtrasoDias);

        var vencimento = new DateOnly(2026, 11, 1);
        var dia10 = RegrasCobranca.Avaliar(StatusAssinatura.Atrasada, null, 0, vencimento, Agora, new DateOnly(2026, 11, 11));
        Assert.True(dia10.PodeEmitirEmProducao);
        Assert.Contains("para em 12/11/2026", dia10.Aviso);

        var dia11 = RegrasCobranca.Avaliar(StatusAssinatura.Atrasada, null, 0, vencimento, Agora, new DateOnly(2026, 11, 12));
        Assert.False(dia11.PodeEmitirEmProducao);
    }

    [Fact]
    public void Cancelada_bloqueia()
    {
        Assert.False(RegrasCobranca.Avaliar(StatusAssinatura.Cancelada, null, 0, null, Agora, Hoje).PodeEmitirEmProducao);
    }
}
