using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Contratos;
using Xunit;

namespace NfseSaaS.Tests;

public sealed class ReajusteContratoTests
{
    [Theory]
    [InlineData(1000, 4.5678, 1045.68)]
    [InlineData(150, 0, 150)]
    [InlineData(100, -2.5, 97.5)]
    [InlineData(99.99, 3.335, 103.32)]
    public void CalcularValor_DuasCasasArredondamentoComercial(decimal atual, decimal percentual, decimal esperado)
    {
        Assert.Equal(esperado, ReajusteContrato.CalcularValor(atual, percentual));
    }

    [Fact]
    public void PercentualEfetivo_NegativoSemPermissao_MantemValor()
    {
        Assert.Equal(0m, ReajusteContrato.PercentualEfetivo(-1.2m, aplicarIndiceNegativo: false));
        Assert.Equal(-1.2m, ReajusteContrato.PercentualEfetivo(-1.2m, aplicarIndiceNegativo: true));
        Assert.Equal(4.5m, ReajusteContrato.PercentualEfetivo(4.5m, aplicarIndiceNegativo: false));
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void ReajusteAutomatico_ContratoSobrescreveEmpresa(bool? doContrato, bool daEmpresa, bool esperado)
    {
        var contrato = new Contrato { ReajusteAutomatico = doContrato };
        var empresa = new Empresa { AplicarReajusteAutomatico = daEmpresa };
        Assert.Equal(esperado, ReajusteContrato.ReajusteAutomaticoEfetivo(contrato, empresa));
    }

    private static bool Valido(DecidirReajusteRequest r) => new DecidirReajusteRequestValidator().Validate(r).IsValid;

    [Fact]
    public void Decisao_AplicarCalculado_NaoPedeMotivo()
    {
        Assert.True(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.AplicarCalculado, null, null, null)));
    }

    [Fact]
    public void Decisao_NegociadoERecusa_ExigemMotivo()
    {
        Assert.False(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.AplicarNegociado, 3m, null, null)));
        Assert.False(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.Recusar, null, null, " ")));
        Assert.True(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.Recusar, null, null, "cliente não aceitou")));
    }

    [Fact]
    public void Decisao_Negociado_ExigeUmEApenasUmEntrePercentualEValor()
    {
        Assert.False(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.AplicarNegociado, null, null, "acordo")));
        Assert.False(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.AplicarNegociado, 3m, 1030m, "acordo")));
        Assert.True(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.AplicarNegociado, 3m, null, "acordo")));
        Assert.True(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.AplicarNegociado, null, 1030m, "acordo")));
        Assert.False(Valido(new DecidirReajusteRequest(AcaoDecisaoReajuste.AplicarNegociado, null, 0m, "acordo")));
    }
}
