using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>B2: quem entra no reajuste de assinantes, data mínima e o que cancela na data.</summary>
public class RegrasReajusteAssinaturaTests
{
    private static readonly DateOnly Hoje = new(2027, 3, 10);
    private static readonly DateOnly Data = Hoje.AddDays(40);

    private static SituacaoReajuste Normal() => new(
        StatusAssinatura.Ativa, TemAssinaturaNoGateway: true, CobrancaValeParaConta: true, Plano: 1, PrecoMensal: 29m,
        Fundador: false, AssinadaEm: new DateOnly(2026, 1, 15), UltimoReajusteEm: null, TemReajusteAgendado: false);

    [Fact]
    public void Data_precisa_de_30_dias_de_aviso_e_no_maximo_180()
    {
        Assert.Equal(Hoje.AddDays(30), RegrasReajusteAssinatura.DataMinima(Hoje));
        Assert.Null(RegrasReajusteAssinatura.ProblemaNaData(Hoje.AddDays(30), Hoje));
        Assert.Contains("09/04/2027", RegrasReajusteAssinatura.ProblemaNaData(Hoje.AddDays(29), Hoje));
        Assert.NotNull(RegrasReajusteAssinatura.ProblemaNaData(Hoje.AddDays(181), Hoje));
    }

    [Fact]
    public void Assinante_comum_ha_mais_de_um_ano_entra() =>
        Assert.Null(RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal(), 1, 35m, Data));

    [Fact]
    public void Fundador_fica_de_fora() =>
        Assert.Contains("fundador", RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal() with { Fundador = true }, 1, 35m, Data));

    [Theory]
    [InlineData(29)]
    [InlineData(40)]
    public void Quem_ja_paga_igual_ou_mais_fica_de_fora(decimal atual) =>
        Assert.Contains("igual ou acima", RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal() with { PrecoMensal = atual }, 1, 29m, Data));

    [Fact]
    public void Doze_meses_desde_a_assinatura_ou_do_ultimo_reajuste()
    {
        var recente = Normal() with { AssinadaEm = Data.AddMonths(-11) };
        Assert.Contains("a assinatura", RegrasReajusteAssinatura.MotivoForaDoReajuste(recente, 1, 35m, Data));

        var reajustada = Normal() with { UltimoReajusteEm = Data.AddMonths(-6) };
        Assert.Contains("o último reajuste", RegrasReajusteAssinatura.MotivoForaDoReajuste(reajustada, 1, 35m, Data));

        var exatoUmAno = Normal() with { AssinadaEm = Data.AddMonths(-12) };
        Assert.Null(RegrasReajusteAssinatura.MotivoForaDoReajuste(exatoUmAno, 1, 35m, Data));
    }

    [Fact]
    public void Cancelada_outro_plano_cobranca_desligada_ou_ja_agendado_ficam_de_fora()
    {
        Assert.NotNull(RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal() with { Status = StatusAssinatura.Cancelada }, 1, 35m, Data));
        Assert.NotNull(RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal() with { TemAssinaturaNoGateway = false }, 1, 35m, Data));
        Assert.NotNull(RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal() with { Plano = 2 }, 1, 35m, Data));
        Assert.NotNull(RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal() with { CobrancaValeParaConta = false }, 1, 35m, Data));
        Assert.NotNull(RegrasReajusteAssinatura.MotivoForaDoReajuste(Normal() with { TemReajusteAgendado = true }, 1, 35m, Data));
    }

    [Fact]
    public void Na_data_qualquer_mudanca_na_assinatura_cancela_o_reajuste()
    {
        Assert.Null(RegrasReajusteAssinatura.MotivoNaoAplicar(Normal(), 1, 29m));
        Assert.Contains("trocou de plano", RegrasReajusteAssinatura.MotivoNaoAplicar(Normal() with { Plano = 2 }, 1, 29m));
        Assert.Contains("cancelada", RegrasReajusteAssinatura.MotivoNaoAplicar(Normal() with { Status = StatusAssinatura.Cancelada }, 1, 29m));
        Assert.Contains("valor", RegrasReajusteAssinatura.MotivoNaoAplicar(Normal() with { PrecoMensal = 25m }, 1, 29m));
        Assert.Contains("fundador", RegrasReajusteAssinatura.MotivoNaoAplicar(Normal() with { Fundador = true }, 1, 29m));
        // Atraso não impede: o Asaas só muda faturas em aberto (as vencidas ficam como estão).
        Assert.Null(RegrasReajusteAssinatura.MotivoNaoAplicar(Normal() with { Status = StatusAssinatura.Atrasada }, 1, 29m));
    }

    [Fact]
    public void Email_traz_de_para_e_data_e_codifica_o_nome_do_plano()
    {
        var (assunto, corpo) = MensagensReajusteAssinatura.Montar("Mico <b>", 29m, 35m, new DateOnly(2027, 4, 20), "https://app/Assinatura", "suporte@x.com");
        Assert.Contains("20/04/2027", assunto);
        Assert.Contains("29,00", corpo);
        Assert.Contains("35,00", corpo);
        Assert.Contains("Mico &lt;b&gt;", corpo);
        Assert.Contains("https://app/Assinatura", corpo);
        Assert.Contains("8.5", corpo);
    }
}
