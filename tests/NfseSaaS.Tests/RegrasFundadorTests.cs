using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Campanha de fundador: reserva para quem entra, prazo depois do teste, data-fim e grupo.</summary>
public class RegrasFundadorTests
{
    private static readonly DateTimeOffset Agora = new(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Contador_concorre_as_vagas_dos_escritorios_e_empresa_as_das_empresas()
    {
        Assert.Equal(PublicoPlano.Escritorios, RegrasFundador.GrupoDoPerfil(PerfilConta.Contador));
        Assert.Equal(PublicoPlano.Empresas, RegrasFundador.GrupoDoPerfil(PerfilConta.Empresa));
    }

    [Fact]
    public void Campanha_vale_ate_o_ultimo_dia_inclusive_e_sem_data_vale_sempre()
    {
        var fim = new DateOnly(2027, 1, 31);
        Assert.True(RegrasFundador.CampanhaAbertaNaData(fim, fim));
        Assert.False(RegrasFundador.CampanhaAbertaNaData(fim, fim.AddDays(1)));
        Assert.True(RegrasFundador.CampanhaAbertaNaData(null, new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Reserva_vale_ate_7_dias_depois_do_fim_do_teste()
    {
        var reservada = Agora.AddDays(-60);
        // Teste começou há 37 dias: acabou há 7, ainda vale (limite exato).
        Assert.True(RegrasFundador.ReservaValida(reservada, false, false, StatusAssinatura.Teste, Agora.AddDays(-37), Agora));
        // Há 37 dias e 1 minuto: passou.
        Assert.False(RegrasFundador.ReservaValida(reservada, false, false, StatusAssinatura.Teste, Agora.AddDays(-37).AddMinutes(-1), Agora));
        // Teste que não começou: o prazo nem começou a correr.
        Assert.True(RegrasFundador.ReservaValida(reservada, false, false, StatusAssinatura.Teste, null, Agora));
        // Teste prorrogado pelo operador: a reserva acompanha.
        Assert.True(RegrasFundador.ReservaValida(reservada, false, false, StatusAssinatura.Teste, Agora.AddDays(-50), Agora, diasExtrasTeste: 15));
        Assert.Equal(new DateOnly(2027, 1, 31), RegrasFundador.ReservaValeAte(false, Agora, diasExtrasTeste: 15));
        // Reserva do beta: sem prazo.
        Assert.True(RegrasFundador.ReservaValida(reservada, true, false, StatusAssinatura.Teste, Agora.AddDays(-300), Agora));
    }

    [Fact]
    public void Reserva_nao_vale_sem_reserva_para_quem_ja_e_fundador_nem_cancelada()
    {
        Assert.False(RegrasFundador.ReservaValida(null, true, false, StatusAssinatura.Teste, null, Agora));
        Assert.False(RegrasFundador.ReservaValida(Agora, true, true, StatusAssinatura.Ativa, null, Agora));
        Assert.False(RegrasFundador.ReservaValida(Agora, true, false, StatusAssinatura.Cancelada, null, Agora));
    }

    [Fact]
    public void Data_da_reserva_so_existe_depois_que_o_teste_comeca()
    {
        Assert.Null(RegrasFundador.ReservaValeAte(semPrazo: false, testeIniciadoEm: null));
        Assert.Null(RegrasFundador.ReservaValeAte(semPrazo: true, testeIniciadoEm: Agora));
        // 10/12 às 9h de Brasília + 30 + 7 dias = 16/01/2027.
        Assert.Equal(new DateOnly(2027, 1, 16), RegrasFundador.ReservaValeAte(false, Agora));
    }

    [Fact]
    public void So_reserva_quem_nunca_assinou_tem_email_confirmado_e_ainda_ha_vaga()
    {
        Assert.True(RegrasFundador.PodeReservar(true, true, false, null, false, null, vagasRestantes: 1));
        Assert.False(RegrasFundador.PodeReservar(false, true, false, null, false, null, 1));     // e-mail não confirmado
        Assert.False(RegrasFundador.PodeReservar(true, false, false, null, false, null, 1));     // conta suspensa
        Assert.False(RegrasFundador.PodeReservar(true, true, true, null, false, null, 1));       // isenta
        Assert.False(RegrasFundador.PodeReservar(true, true, false, Agora, false, null, 1));     // já reservou
        Assert.False(RegrasFundador.PodeReservar(true, true, false, null, true, null, 1));       // já é fundador
        Assert.False(RegrasFundador.PodeReservar(true, true, false, null, false, Agora, 1));     // já assinou um dia
        Assert.False(RegrasFundador.PodeReservar(true, true, false, null, false, null, 0));      // sem vaga
    }

    [Fact]
    public void Assina_como_fundador_pela_reserva_do_grupo_mesmo_sem_vaga_sobrando()
    {
        Assert.True(RegrasFundador.AssinaComoFundador(true, PublicoPlano.Empresas, PublicoPlano.Empresas, 0));
        // Reserva de outro grupo não vale; sem vaga no grupo do plano, preço cheio.
        Assert.False(RegrasFundador.AssinaComoFundador(true, PublicoPlano.Escritorios, PublicoPlano.Empresas, 0));
        // Sem reserva, vale a vaga sobrando.
        Assert.True(RegrasFundador.AssinaComoFundador(false, null, PublicoPlano.Empresas, 3));
        Assert.False(RegrasFundador.AssinaComoFundador(false, null, PublicoPlano.Empresas, 0));
    }
}
