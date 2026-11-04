using NfseSaaS.Application.UseCases.Certificados;
using Xunit;

namespace NfseSaaS.Tests;

public class FaixaAvisoCertificadoTests
{
    [Theory]
    [InlineData(90, null)]
    [InlineData(31, null)]
    [InlineData(30, 30)]
    [InlineData(16, 30)]
    [InlineData(15, 15)]
    [InlineData(8, 15)]
    [InlineData(7, 7)]
    [InlineData(1, 7)]
    [InlineData(0, 7)]
    [InlineData(-1, FaixaAvisoCertificado.Vencido)]
    [InlineData(-200, FaixaAvisoCertificado.Vencido)]
    public void Faixa_segue_30_15_7_e_vencido(int diasRestantes, int? faixaEsperada)
    {
        Assert.Equal(faixaEsperada, FaixaAvisoCertificado.Calcular(diasRestantes));
    }

    [Fact]
    public void Job_atrasado_ainda_avisa_na_faixa_certa()
    {
        // Se o job não rodou exatamente no 30º dia, o aviso de 30 dias sai
        // no dia seguinte (faixa), e não se perde.
        Assert.Equal(30, FaixaAvisoCertificado.Calcular(28));
    }
}
