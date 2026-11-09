using NfseSaaS.Application.UseCases.DadosConta;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>D2 parte 2 — encerramento automático: datas, avisos e chave gravada no e-mail.</summary>
public class RegrasEncerramentoAutomaticoTests
{
    private static readonly DateTimeOffset Cancelada = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero); // 10/11 em Brasília

    [Fact]
    public void Data_prevista_conta_do_fim_do_acesso_e_nunca_menos_de_30_dias()
    {
        Assert.Equal(new DateOnly(2027, 2, 8), RegrasEncerramentoAutomatico.DataPrevista(Cancelada, null, 90));
        // Acesso pago até o fim do mês: conta de lá.
        Assert.Equal(new DateOnly(2027, 3, 1), RegrasEncerramentoAutomatico.DataPrevista(Cancelada, new DateOnly(2026, 11, 30), 91));
        // Configuração pequena demais vira 30.
        Assert.Equal(new DateOnly(2026, 12, 10), RegrasEncerramentoAutomatico.DataPrevista(Cancelada, null, 5));
    }

    [Fact]
    public void Avisa_15_dias_antes_avisa_de_novo_3_dias_antes_e_so_entao_encerra()
    {
        var prevista = new DateOnly(2027, 2, 8);
        Assert.Equal(EtapaEncerramentoAutomatico.Nada, RegrasEncerramentoAutomatico.Etapa(prevista.AddDays(-16), prevista, null, false));
        Assert.Equal(EtapaEncerramentoAutomatico.AvisoAntecipado, RegrasEncerramentoAutomatico.Etapa(prevista.AddDays(-15), prevista, null, false));
        Assert.Equal(EtapaEncerramentoAutomatico.Nada, RegrasEncerramentoAutomatico.Etapa(prevista.AddDays(-10), prevista, prevista, false));
        Assert.Equal(EtapaEncerramentoAutomatico.AvisoFinal, RegrasEncerramentoAutomatico.Etapa(prevista.AddDays(-3), prevista, prevista, false));
        Assert.Equal(EtapaEncerramentoAutomatico.Nada, RegrasEncerramentoAutomatico.Etapa(prevista.AddDays(-2), prevista, prevista, true));
        Assert.Equal(EtapaEncerramentoAutomatico.Encerrar, RegrasEncerramentoAutomatico.Etapa(prevista, prevista, prevista, true));
    }

    [Fact]
    public void Nunca_encerra_sem_aviso_mesmo_com_a_data_ja_passada()
    {
        var hoje = new DateOnly(2027, 6, 1);
        var prevista = new DateOnly(2027, 2, 8); // ligaram a configuração bem depois
        Assert.Equal(EtapaEncerramentoAutomatico.AvisoAntecipado, RegrasEncerramentoAutomatico.Etapa(hoje, prevista, null, false));
        var prometida = RegrasEncerramentoAutomatico.DataPrometida(prevista, hoje);
        Assert.Equal(hoje.AddDays(15), prometida);
        Assert.Equal(EtapaEncerramentoAutomatico.Nada, RegrasEncerramentoAutomatico.Etapa(hoje.AddDays(1), prevista, prometida, false));
        Assert.Equal(new DateOnly(2027, 3, 1), RegrasEncerramentoAutomatico.DataPrometida(new DateOnly(2027, 3, 1), new DateOnly(2027, 2, 1)));
    }

    [Fact]
    public void A_data_prometida_fica_na_chave_do_email_e_cada_cancelamento_e_um_ciclo()
    {
        var tenant = Guid.NewGuid();
        var prefixo = RegrasEncerramentoAutomatico.ChavePrefixo(tenant, Cancelada);
        var antecipado = RegrasEncerramentoAutomatico.ChaveAviso(prefixo, new DateOnly(2027, 2, 8), EtapaEncerramentoAutomatico.AvisoAntecipado);
        var final = RegrasEncerramentoAutomatico.ChaveAviso(prefixo, new DateOnly(2027, 2, 8), EtapaEncerramentoAutomatico.AvisoFinal);

        Assert.StartsWith(prefixo, antecipado);
        Assert.Equal(new DateOnly(2027, 2, 8), RegrasEncerramentoAutomatico.DataNaChave(antecipado));
        Assert.False(RegrasEncerramentoAutomatico.EhAvisoFinal(antecipado));
        Assert.True(RegrasEncerramentoAutomatico.EhAvisoFinal(final));
        Assert.Null(RegrasEncerramentoAutomatico.DataNaChave("encerramento:abc:20270208"));
        Assert.NotEqual(prefixo, RegrasEncerramentoAutomatico.ChavePrefixo(tenant, Cancelada.AddSeconds(1)));
    }
}
