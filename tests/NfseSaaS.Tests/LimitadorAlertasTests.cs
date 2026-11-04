using NfseSaaS.Infrastructure.Monitoramento;
using Xunit;

namespace NfseSaaS.Tests;

public class LimitadorAlertasTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Primeira_ocorrencia_vira_email()
    {
        var limitador = new LimitadorAlertas();

        Assert.Equal((true, 0), limitador.Avaliar("erro-a", Inicio));
    }

    [Fact]
    public void Repeticao_dentro_da_janela_e_segurada_e_contada_no_proximo_email()
    {
        var limitador = new LimitadorAlertas();
        limitador.Avaliar("erro-a", Inicio);

        Assert.False(limitador.Avaliar("erro-a", Inicio.AddMinutes(5)).Enviar);
        Assert.False(limitador.Avaliar("erro-a", Inicio.AddMinutes(10)).Enviar);

        Assert.Equal((true, 2), limitador.Avaliar("erro-a", Inicio.Add(LimitadorAlertas.JanelaPorChave)));
    }

    [Fact]
    public void Chaves_diferentes_nao_se_seguram()
    {
        var limitador = new LimitadorAlertas();
        limitador.Avaliar("erro-a", Inicio);

        Assert.True(limitador.Avaliar("erro-b", Inicio.AddSeconds(1)).Enviar);
    }

    [Fact]
    public void Teto_por_hora_vale_para_o_total()
    {
        var limitador = new LimitadorAlertas();
        for (var i = 0; i < LimitadorAlertas.MaximoPorHora; i++)
            Assert.True(limitador.Avaliar($"erro-{i}", Inicio.AddSeconds(i)).Enviar);

        Assert.False(limitador.Avaliar("erro-novo", Inicio.AddMinutes(30)).Enviar);
        Assert.True(limitador.Avaliar("erro-novo", Inicio.AddHours(1).AddMinutes(1)).Enviar);
    }
}
