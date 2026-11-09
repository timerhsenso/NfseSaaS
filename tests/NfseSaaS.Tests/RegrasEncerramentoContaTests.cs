using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>D2 — encerramento da conta: prazo do expurgo, quem pode, mensagem do login.</summary>
public class RegrasEncerramentoContaTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 9);

    [Fact]
    public void Dados_sao_apagados_90_dias_depois_do_encerramento()
    {
        Assert.Equal(new DateOnly(2027, 2, 7), RegrasEncerramentoConta.ExpurgoPrevisto(Hoje));

        var encerrada = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(RegrasEncerramentoConta.DeveExpurgar(encerrada, Hoje, null, Hoje));
        Assert.True(RegrasEncerramentoConta.DeveExpurgar(encerrada, Hoje.AddDays(-1), null, Hoje));
        Assert.False(RegrasEncerramentoConta.DeveExpurgar(encerrada, Hoje.AddDays(1), null, Hoje));
        Assert.False(RegrasEncerramentoConta.DeveExpurgar(encerrada, Hoje, DateTimeOffset.UtcNow, Hoje)); // já apagou
        Assert.False(RegrasEncerramentoConta.DeveExpurgar(null, Hoje, null, Hoje)); // reaberta
    }

    [Fact]
    public void So_administrador_encerra_e_nunca_a_conta_da_plataforma()
    {
        Assert.Equal(RegrasEncerramentoConta.MotivoSoAdministrador, RegrasEncerramentoConta.MotivoParaNaoEncerrar(false, false, false));
        Assert.Equal(RegrasEncerramentoConta.MotivoContaDaPlataforma, RegrasEncerramentoConta.MotivoParaNaoEncerrar(true, false, true));
        Assert.Equal(RegrasEncerramentoConta.MotivoJaEncerrada, RegrasEncerramentoConta.MotivoParaNaoEncerrar(true, true, false));
        Assert.Null(RegrasEncerramentoConta.MotivoParaNaoEncerrar(true, false, false));
    }

    [Fact]
    public void Login_da_conta_encerrada_diz_ate_quando_da_para_reabrir()
    {
        var encerrada = new DateTimeOffset(2026, 11, 9, 2, 0, 0, TimeSpan.Zero); // 08/11 em Brasília
        var antes = RegrasEncerramentoConta.MensagemLogin(encerrada, new DateOnly(2027, 2, 6), expurgada: false, "lgpd@x");
        Assert.Contains("encerrada em 08/11/2026", antes);
        Assert.Contains("até 06/02/2027", antes);
        Assert.Contains("lgpd@x", antes);

        var depois = RegrasEncerramentoConta.MensagemLogin(encerrada, new DateOnly(2027, 2, 6), expurgada: true, "lgpd@x");
        Assert.DoesNotContain("reabrir", depois);
    }

    [Fact]
    public void Motivo_gravado_conforme_a_origem()
    {
        Assert.Equal("Conta encerrada a pedido do cliente.", RegrasEncerramentoConta.MotivoBloqueio(OrigemEncerramento.Cliente, "ignorado"));
        Assert.Equal("Conta encerrada pela plataforma: fraude", RegrasEncerramentoConta.MotivoBloqueio(OrigemEncerramento.Plataforma, " fraude "));
        Assert.Equal("Conta encerrada pela plataforma.", RegrasEncerramentoConta.MotivoBloqueio(OrigemEncerramento.Plataforma, null));
        Assert.StartsWith("Conta encerrada automaticamente", RegrasEncerramentoConta.MotivoBloqueio(OrigemEncerramento.Automatico, null));
    }
}
