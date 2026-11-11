using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Infrastructure.Contratos;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Nota Mensal com valor por serviço: sugestão, o que pode mudar, gravar no contrato e a composição na nota.</summary>
public class RegrasValorNotaMensalTests
{
    private static readonly Guid Manutencao = Guid.NewGuid();
    private static readonly Guid HelpDesk = Guid.NewGuid();

    private static IReadOnlyList<LinhaContratoValor> Linhas() => new[]
    {
        new LinhaContratoValor(Manutencao, "Manutenção", 1500m, ValorVariavel: false),
        new LinhaContratoValor(HelpDesk, "HelpDesk", 1m, ValorVariavel: true)
    };

    [Fact]
    public void Sugestao_variavel_usa_o_ultimo_e_fixo_o_contrato()
    {
        Assert.Equal(800m, RegrasValorNotaMensal.Sugestao(1m, variavel: true, ultimoValor: 800m));
        Assert.Equal(1m, RegrasValorNotaMensal.Sugestao(1m, variavel: true, ultimoValor: null));
        Assert.Equal(1500m, RegrasValorNotaMensal.Sugestao(1500m, variavel: false, ultimoValor: 900m));
    }

    [Fact]
    public void Sem_pedido_vale_o_valor_do_contrato()
    {
        var decididos = RegrasValorNotaMensal.Decidir(Linhas(), null, permiteAlterarNaEmissao: false);
        Assert.Equal(new[] { 1500m, 1m }, decididos.Select(d => d.Valor));
        Assert.All(decididos, d => Assert.False(d.GravarNoContrato));
    }

    [Fact]
    public void Variavel_muda_sempre_e_nunca_grava_no_contrato()
    {
        var decididos = RegrasValorNotaMensal.Decidir(Linhas(),
            new[] { new ValorServicoNotaMensalRequest(HelpDesk, 830m, GravarNoContrato: true) }, permiteAlterarNaEmissao: false);
        var help = decididos.Single(d => d.ContratoServicoId == HelpDesk);
        Assert.Equal(830m, help.Valor);
        Assert.False(help.GravarNoContrato);
    }

    [Fact]
    public void Fixo_alterado_so_nesta_nota_exige_o_contrato_permitir()
    {
        var pedido = new[] { new ValorServicoNotaMensalRequest(Manutencao, 1600m) };

        var erro = Assert.Throws<InvalidOperationException>(() => RegrasValorNotaMensal.Decidir(Linhas(), pedido, permiteAlterarNaEmissao: false));
        Assert.Contains("gravar no contrato", erro.Message);

        var soNesta = RegrasValorNotaMensal.Decidir(Linhas(), pedido, permiteAlterarNaEmissao: true).Single(d => d.ContratoServicoId == Manutencao);
        Assert.Equal(1600m, soNesta.Valor);
        Assert.False(soNesta.GravarNoContrato);

        var gravando = RegrasValorNotaMensal.Decidir(Linhas(),
            new[] { new ValorServicoNotaMensalRequest(Manutencao, 1600m, GravarNoContrato: true) }, permiteAlterarNaEmissao: false)
            .Single(d => d.ContratoServicoId == Manutencao);
        Assert.True(gravando.GravarNoContrato);
    }

    [Fact]
    public void Fixo_igual_ao_contrato_nao_grava_nem_exige_permissao()
    {
        var decidido = RegrasValorNotaMensal.Decidir(Linhas(),
            new[] { new ValorServicoNotaMensalRequest(Manutencao, 1500m, GravarNoContrato: true) }, permiteAlterarNaEmissao: false)
            .Single(d => d.ContratoServicoId == Manutencao);
        Assert.False(decidido.GravarNoContrato);
    }

    [Fact]
    public void Servico_que_nao_e_do_contrato_ou_valor_zero_sao_recusados()
    {
        Assert.Throws<InvalidOperationException>(() => RegrasValorNotaMensal.Decidir(Linhas(),
            new[] { new ValorServicoNotaMensalRequest(Guid.NewGuid(), 10m) }, true));
        Assert.Throws<InvalidOperationException>(() => RegrasValorNotaMensal.Decidir(Linhas(),
            new[] { new ValorServicoNotaMensalRequest(HelpDesk, 0m) }, true));
    }

    [Theory]
    [InlineData(1, 1500, 1, 1500)]
    [InlineData(10, 1500, 10, 150)]
    [InlineData(3, 100, 1, 100)]
    public void Recompor_linha_mantem_a_quantidade_quando_fecha_em_centavos(decimal qtd, decimal total, decimal qtdEsperada, decimal unitarioEsperado)
    {
        var (quantidade, unitario) = RegrasValorNotaMensal.RecomporLinha(qtd, total);
        Assert.Equal(qtdEsperada, quantidade);
        Assert.Equal(unitarioEsperado, unitario);
        Assert.Equal(total, quantidade * unitario);
    }

    [Fact]
    public void Composicao_so_e_gravada_quando_fecha_com_o_total()
    {
        var itens = new[]
        {
            new ItemComposicaoNfse(Manutencao, Guid.NewGuid(), "Manutenção", 1500m),
            new ItemComposicaoNfse(HelpDesk, Guid.NewGuid(), "HelpDesk", 800m)
        };
        Assert.Null(ComposicaoValoresNfse.Serializar(itens, 2299m));
        Assert.Null(ComposicaoValoresNfse.Serializar(null, 10m));

        var json = ComposicaoValoresNfse.Serializar(itens, 2300m);
        Assert.Equal(itens, ComposicaoValoresNfse.Desserializar(json));
        Assert.Empty(ComposicaoValoresNfse.Desserializar("não é json"));
    }
}
