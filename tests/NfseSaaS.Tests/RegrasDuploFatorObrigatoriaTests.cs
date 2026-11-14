using NfseSaaS.Application.UseCases.Seguranca;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>SEG1 — quem precisa ligar a verificação em duas etapas e até quando.</summary>
public class RegrasDuploFatorObrigatoriaTests
{
    private static readonly DateOnly Inicio = RegrasDuploFator.ObrigatoriaParaAdministradoresDesde;

    [Fact]
    public void Usuario_comum_sem_exigencia_da_conta_nao_precisa()
    {
        var r = RegrasDuploFator.Avaliar(false, operador: false, administrador: false, contaExigeDesde: null, Inicio, Inicio.AddDays(100));
        Assert.False(r.Exigida);
        Assert.False(r.Bloqueia);
    }

    [Fact]
    public void Operador_nao_tem_prazo()
    {
        var r = RegrasDuploFator.Avaliar(false, operador: true, administrador: true, contaExigeDesde: null, Inicio, Inicio);
        Assert.Equal(MotivoExigenciaDuploFator.Operador, r.Motivo);
        Assert.Null(r.Prazo);
        Assert.True(r.Bloqueia);
    }

    [Fact]
    public void Administrador_antigo_tem_prazo_a_partir_da_regra()
    {
        var entrou = Inicio.AddYears(-1);
        var prazo = Inicio.AddDays(RegrasDuploFator.DiasPrazoAdesao);

        var noPrazo = RegrasDuploFator.Avaliar(false, false, administrador: true, null, entrou, prazo);
        Assert.Equal(prazo, noPrazo.Prazo);
        Assert.True(noPrazo.Pendente);
        Assert.False(noPrazo.Bloqueia); // o último dia ainda vale

        Assert.True(RegrasDuploFator.Avaliar(false, false, true, null, entrou, prazo.AddDays(1)).Bloqueia);
    }

    [Fact]
    public void Administrador_novo_tem_prazo_a_partir_do_primeiro_acesso()
    {
        var entrou = Inicio.AddDays(40);
        var r = RegrasDuploFator.Avaliar(false, false, true, null, entrou, entrou);
        Assert.Equal(entrou.AddDays(RegrasDuploFator.DiasPrazoAdesao), r.Prazo);
    }

    [Fact]
    public void Exigencia_da_conta_vale_para_todos_e_o_prazo_mais_curto_ganha()
    {
        var contaDesde = Inicio.AddDays(-30);
        var entrou = Inicio.AddDays(-60);

        var comum = RegrasDuploFator.Avaliar(false, false, false, contaDesde, entrou, Inicio);
        Assert.Equal(MotivoExigenciaDuploFator.Conta, comum.Motivo);
        Assert.Equal(contaDesde.AddDays(RegrasDuploFator.DiasPrazoAdesao), comum.Prazo);
        Assert.True(comum.Bloqueia);

        // Administrador: a exigência da conta (mais antiga) vence a da plataforma.
        var admin = RegrasDuploFator.Avaliar(false, false, true, contaDesde, entrou, Inicio);
        Assert.Equal(MotivoExigenciaDuploFator.Conta, admin.Motivo);
    }

    [Fact]
    public void Ligada_nunca_bloqueia_mas_continua_exigida()
    {
        var r = RegrasDuploFator.Avaliar(true, operador: true, administrador: true, Inicio, Inicio, Inicio.AddYears(1));
        Assert.True(r.Exigida);
        Assert.False(r.Pendente);
        Assert.False(r.Bloqueia);
    }

    [Fact]
    public void Aviso_do_prazo_conta_os_dias()
    {
        var r = RegrasDuploFator.Avaliar(false, false, true, null, Inicio, Inicio);
        Assert.Contains("faltam 14 dias", RegrasDuploFator.TextoAvisoPrazo(r, Inicio));
        Assert.Contains("até amanhã", RegrasDuploFator.TextoAvisoPrazo(r, r.Prazo!.Value.AddDays(-1)));
        Assert.Contains("até hoje", RegrasDuploFator.TextoAvisoPrazo(r, r.Prazo!.Value));
        Assert.Contains("administradores", RegrasDuploFator.TextoAvisoPrazo(r, Inicio));
    }
}
