using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Regras do prêmio do "Indique e ganhe" (sem banco nem gateway).</summary>
public class RegrasPremioIndicacaoTests
{
    private static readonly Guid Conta = Guid.NewGuid();
    private static readonly Guid Indicador = Guid.NewGuid();

    [Fact]
    public void So_o_primeiro_pagamento_de_conta_indicada_gera_premio()
    {
        Assert.True(RegrasPremioIndicacao.GeraPremio(true, Conta, Indicador));
        Assert.False(RegrasPremioIndicacao.GeraPremio(false, Conta, Indicador));
        Assert.False(RegrasPremioIndicacao.GeraPremio(true, Conta, null));
        Assert.False(RegrasPremioIndicacao.GeraPremio(true, Conta, Conta));
    }

    [Fact]
    public void Aplica_so_em_assinatura_em_dia_com_assinatura_no_gateway()
    {
        Assert.Null(RegrasPremioIndicacao.MotivoEspera(StatusAssinatura.Ativa, true, isenta: false, contaAtiva: true));

        Assert.Equal(RegrasPremioIndicacao.MotivoSemAssinatura, RegrasPremioIndicacao.MotivoEspera(null, false, false, true));
        Assert.Equal(RegrasPremioIndicacao.MotivoSemAssinatura, RegrasPremioIndicacao.MotivoEspera(StatusAssinatura.Teste, false, false, true));
        // Assinou mas ainda não pagou a primeira fatura.
        Assert.Equal(RegrasPremioIndicacao.MotivoSemAssinatura, RegrasPremioIndicacao.MotivoEspera(StatusAssinatura.Teste, true, false, true));
        Assert.Equal(RegrasPremioIndicacao.MotivoAtrasada, RegrasPremioIndicacao.MotivoEspera(StatusAssinatura.Atrasada, true, false, true));
        Assert.Equal(RegrasPremioIndicacao.MotivoCancelada, RegrasPremioIndicacao.MotivoEspera(StatusAssinatura.Cancelada, true, false, true));
        Assert.Equal(RegrasPremioIndicacao.MotivoSuspensa, RegrasPremioIndicacao.MotivoEspera(StatusAssinatura.Ativa, true, false, contaAtiva: false));
        Assert.Equal(RegrasPremioIndicacao.MotivoIsenta, RegrasPremioIndicacao.MotivoEspera(StatusAssinatura.Ativa, true, isenta: true, contaAtiva: true));
    }

    [Theory]
    [InlineData(1, StatusPremioIndicacao.Pendente)]
    [InlineData(2, StatusPremioIndicacao.Pendente)]
    [InlineData(3, StatusPremioIndicacao.Erro)]
    public void Recusa_do_gateway_tenta_de_novo_ate_o_limite(int tentativas, StatusPremioIndicacao esperado)
    {
        Assert.Equal(esperado, RegrasPremioIndicacao.StatusAposRecusa(tentativas));
    }
}
