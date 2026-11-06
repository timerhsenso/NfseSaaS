using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using Xunit;

namespace NfseSaaS.Tests;

public class RegrasAvisoCobrancaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 11, 10);
    private static readonly Guid Conta = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static EstadoAvisoCobranca Estado(
        StatusAssinatura status = StatusAssinatura.Teste,
        DateTimeOffset? testeIniciadoEm = null,
        int notas = 0,
        string? assinaturaId = null,
        DateOnly? atrasadaDesde = null,
        string? url = null,
        DateOnly? vencimento = null,
        decimal? valor = null,
        DateTimeOffset? pagoEm = null,
        DateTimeOffset? canceladaEm = null) =>
        new(Conta, status, testeIniciadoEm, notas, assinaturaId, atrasadaDesde, url, vencimento, valor, pagoEm, canceladaEm);

    private static IReadOnlyList<TipoAvisoCobranca> Tipos(EstadoAvisoCobranca estado) =>
        RegrasAvisoCobranca.Avaliar(estado, Agora, Hoje).Select(a => a.Tipo).ToList();

    [Fact]
    public void Teste_que_nao_comecou_nao_avisa_nada()
    {
        Assert.Empty(Tipos(Estado()));
        Assert.Null(RegrasAvisoCobranca.Faixa(Estado(), Agora, Hoje));
    }

    [Fact]
    public void Teste_no_meio_nao_avisa()
    {
        Assert.Empty(Tipos(Estado(testeIniciadoEm: Agora.AddDays(-10), notas: 3)));
    }

    [Theory]
    [InlineData(-24, "7d")]   // faltam 6 dias
    [InlineData(-23, "7d")]   // faltam 7 dias
    [InlineData(-28, "2d")]   // faltam 2 dias
    [InlineData(-29.5, "2d")] // falta meio dia
    public void Teste_acabando_avisa_na_faixa_de_7_e_de_2_dias(double diasDesdeInicio, string faixa)
    {
        var avisos = RegrasAvisoCobranca.Avaliar(Estado(testeIniciadoEm: Agora.AddDays(diasDesdeInicio)), Agora, Hoje);

        var aviso = Assert.Single(avisos);
        Assert.Equal(TipoAvisoCobranca.TesteAcabandoDias, aviso.Tipo);
        Assert.EndsWith(":" + faixa, aviso.Chave);
    }

    [Fact]
    public void Teste_com_8_de_10_notas_avisa_uma_vez_por_teste()
    {
        var inicio = Agora.AddDays(-5);
        var com8 = RegrasAvisoCobranca.Avaliar(Estado(testeIniciadoEm: inicio, notas: 8), Agora, Hoje).Single();
        var com9 = RegrasAvisoCobranca.Avaliar(Estado(testeIniciadoEm: inicio, notas: 9), Agora, Hoje).Single();

        Assert.Equal(TipoAvisoCobranca.TesteAcabandoNotas, com8.Tipo);
        Assert.Equal(2, com8.Quantidade);
        Assert.Equal(com8.Chave, com9.Chave);
        Assert.Empty(Tipos(Estado(testeIniciadoEm: inicio, notas: 7)));
    }

    [Fact]
    public void Quem_ja_assinou_nao_recebe_aviso_de_teste_acabando()
    {
        Assert.Empty(Tipos(Estado(testeIniciadoEm: Agora.AddDays(-25), assinaturaId: "sub_1")));
    }

    [Fact]
    public void Teste_acabado_avisa_bloqueio_e_a_faixa_e_de_bloqueio()
    {
        var estado = Estado(testeIniciadoEm: Agora.AddDays(-31));

        Assert.Equal(new[] { TipoAvisoCobranca.EmissaoBloqueada }, Tipos(estado));
        var faixa = RegrasAvisoCobranca.Faixa(estado, Agora, Hoje);
        Assert.Equal(NivelFaixaCobranca.Bloqueio, faixa!.Nivel);
        Assert.False(faixa.MostrarPagar);
    }

    [Fact]
    public void Fatura_criada_vence_amanha_e_vencida_sao_avisos_diferentes_e_exclusivos()
    {
        const string url = "https://asaas/i/1";

        var criada = RegrasAvisoCobranca.Avaliar(Estado(StatusAssinatura.Ativa, url: url, vencimento: Hoje.AddDays(5), valor: 29m), Agora, Hoje).Single();
        var amanha = RegrasAvisoCobranca.Avaliar(Estado(StatusAssinatura.Ativa, url: url, vencimento: Hoje.AddDays(1)), Agora, Hoje).Single();
        var vencida = RegrasAvisoCobranca.Avaliar(Estado(StatusAssinatura.Atrasada, atrasadaDesde: Hoje.AddDays(-2), url: url, vencimento: Hoje.AddDays(-2)), Agora, Hoje).Single();

        Assert.Equal(TipoAvisoCobranca.FaturaCriada, criada.Tipo);
        Assert.Equal(TipoAvisoCobranca.FaturaVenceAmanha, amanha.Tipo);
        Assert.Equal(TipoAvisoCobranca.FaturaVencida, vencida.Tipo);
        Assert.Equal(Hoje.AddDays(-2), vencida.Referencia);
        Assert.Equal(3, new[] { criada.Chave, amanha.Chave, vencida.Chave }.Distinct().Count());
    }

    [Fact]
    public void Primeira_fatura_vencida_sem_nunca_ter_pago_tambem_avisa()
    {
        var estado = Estado(testeIniciadoEm: Agora.AddDays(-5), assinaturaId: "sub_1", url: "https://asaas/i/1", vencimento: Hoje.AddDays(-1));

        Assert.Equal(new[] { TipoAvisoCobranca.FaturaVencida }, Tipos(estado));
        Assert.Equal(NivelFaixaCobranca.Atencao, RegrasAvisoCobranca.Faixa(estado, Agora, Hoje)!.Nivel);
    }

    [Fact]
    public void Atraso_alem_da_tolerancia_avisa_so_o_bloqueio()
    {
        var desde = Hoje.AddDays(-(Planos.ToleranciaAtrasoDias + 1));
        var estado = Estado(StatusAssinatura.Atrasada, atrasadaDesde: desde, url: "https://asaas/i/1", vencimento: desde);

        var aviso = RegrasAvisoCobranca.Avaliar(estado, Agora, Hoje).Single();
        Assert.Equal(TipoAvisoCobranca.EmissaoBloqueada, aviso.Tipo);
        Assert.Contains(":atraso:", aviso.Chave);

        var faixa = RegrasAvisoCobranca.Faixa(estado, Agora, Hoje)!;
        Assert.Equal(NivelFaixaCobranca.Bloqueio, faixa.Nivel);
        Assert.True(faixa.MostrarPagar);
    }

    [Fact]
    public void Pagamento_e_cancelamento_so_avisam_quando_recentes()
    {
        Assert.Equal(new[] { TipoAvisoCobranca.PagamentoConfirmado },
            Tipos(Estado(StatusAssinatura.Ativa, pagoEm: Agora.AddHours(-1))));
        Assert.Empty(Tipos(Estado(StatusAssinatura.Ativa, pagoEm: Agora.AddDays(-10))));

        Assert.Equal(new[] { TipoAvisoCobranca.AssinaturaCancelada },
            Tipos(Estado(StatusAssinatura.Cancelada, assinaturaId: "sub_1", canceladaEm: Agora.AddMinutes(-5), testeIniciadoEm: Agora.AddDays(-40))));
        Assert.Empty(Tipos(Estado(StatusAssinatura.Cancelada, assinaturaId: "sub_1", canceladaEm: Agora.AddDays(-30))));
    }

    [Fact]
    public void Cancelamento_pela_tela_e_pelo_webhook_tem_a_mesma_chave()
    {
        var pelaTela = RegrasAvisoCobranca.Avaliar(Estado(StatusAssinatura.Cancelada, assinaturaId: "sub_1", canceladaEm: Agora.AddMinutes(-5)), Agora, Hoje).Single();
        var depois = RegrasAvisoCobranca.Avaliar(Estado(StatusAssinatura.Cancelada, assinaturaId: "sub_1", canceladaEm: Agora.AddMinutes(-5)), Agora.AddHours(2), Hoje).Single();

        Assert.Equal(pelaTela.Chave, depois.Chave);
    }

    [Fact]
    public void Chaves_cabem_no_EmailLog()
    {
        var estados = new[]
        {
            Estado(testeIniciadoEm: Agora.AddDays(-29), notas: 9),
            Estado(StatusAssinatura.Cancelada, assinaturaId: new string('x', 60), canceladaEm: Agora),
            Estado(StatusAssinatura.Atrasada, atrasadaDesde: Hoje.AddDays(-9), url: "u", vencimento: Hoje.AddDays(-9))
        };

        foreach (var aviso in estados.SelectMany(e => RegrasAvisoCobranca.Avaliar(e, Agora, Hoje)))
            Assert.True(aviso.Chave.Length <= 150, aviso.Chave);
    }

    [Fact]
    public void Faixa_mostra_fatura_em_aberto_com_valor_e_vencimento()
    {
        var faixa = RegrasAvisoCobranca.Faixa(
            Estado(StatusAssinatura.Ativa, url: "https://asaas/i/1", vencimento: Hoje.AddDays(1), valor: 29m), Agora, Hoje)!;

        Assert.Equal(NivelFaixaCobranca.Info, faixa.Nivel);
        Assert.Contains("29,00", faixa.Mensagem);
        Assert.Contains("vence amanhã", faixa.Mensagem);
        Assert.True(faixa.MostrarPagar);
    }

    [Fact]
    public void Faixa_some_com_assinatura_em_dia_e_sem_fatura()
    {
        Assert.Null(RegrasAvisoCobranca.Faixa(Estado(StatusAssinatura.Ativa, pagoEm: Agora.AddDays(-20)), Agora, Hoje));
    }

    [Fact]
    public void Mensagens_codificam_o_link_e_levam_o_botao_certo()
    {
        var estado = Estado(StatusAssinatura.Ativa, url: "https://asaas/i/1?a=1&b=<x>", vencimento: Hoje.AddDays(1), valor: 59m);
        var aviso = RegrasAvisoCobranca.Avaliar(estado, Agora, Hoje).Single();

        var (assunto, corpo) = MensagensAvisoCobranca.Montar(aviso, estado, "Empresa <b>", "https://app/Assinatura");

        Assert.Equal("Sua fatura do micoempresa vence amanhã", assunto);
        Assert.Contains("Pagar a fatura", corpo);
        Assert.Contains("&amp;b=&lt;x&gt;", corpo);
        Assert.Contains("plano Empresa &lt;b&gt;", corpo);
        Assert.Contains("59,00", corpo);
        Assert.DoesNotContain("<b>", corpo);
    }

    [Fact]
    public void Todos_os_tipos_tem_mensagem()
    {
        var estado = Estado(StatusAssinatura.Ativa, url: "https://asaas/i/1", vencimento: Hoje);
        foreach (var tipo in Enum.GetValues<TipoAvisoCobranca>())
        {
            var (assunto, corpo) = MensagensAvisoCobranca.Montar(new AvisoCobranca(tipo, "k", Hoje, 2), estado, null, null);
            Assert.False(string.IsNullOrWhiteSpace(assunto));
            Assert.True(assunto.Length <= 200);
            Assert.Contains("administrador da conta", corpo);
        }
    }
}
