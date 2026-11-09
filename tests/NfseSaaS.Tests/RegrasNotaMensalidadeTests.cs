using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Infrastructure.Cobranca;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>N1: regras da NFS-e da mensalidade (sem banco nem SEFIN).</summary>
public class RegrasNotaMensalidadeTests
{
    [Fact]
    public void Chave_de_idempotencia_e_uma_por_pagamento()
    {
        Assert.Equal("mensalidade:pay_123", RegrasNotaMensalidade.ChaveIdempotencia("pay_123"));
        Assert.NotEqual(RegrasNotaMensalidade.ChaveIdempotencia("pay_1"), RegrasNotaMensalidade.ChaveIdempotencia("pay_2"));
    }

    [Fact]
    public void Descricao_traz_plano_data_e_fatura()
    {
        var d = RegrasNotaMensalidade.Descricao(" Essencial ", new DateOnly(2026, 11, 5), "pay_abc");
        Assert.Equal("Licença de uso do sistema micoempresa — plano Essencial. Mensalidade paga em 05/11/2026 (fatura pay_abc).", d);

        var semPlano = RegrasNotaMensalidade.Descricao(null, new DateOnly(2026, 11, 5), "pay_abc");
        Assert.Equal("Licença de uso do sistema micoempresa. Mensalidade paga em 05/11/2026 (fatura pay_abc).", semPlano);
    }

    [Fact]
    public void Apelido_corta_no_maximo_e_desempata_com_o_fim_do_documento()
    {
        Assert.Equal("Padaria Sol", RegrasNotaMensalidade.Apelido(" Padaria Sol ", "12345678000199", false, 40));
        Assert.Equal("Padaria Sol 0199", RegrasNotaMensalidade.Apelido("Padaria Sol", "12345678000199", true, 40));

        var longo = new string('A', 60);
        Assert.Equal(40, RegrasNotaMensalidade.Apelido(longo, "12345678000199", false, 40).Length);
        var desempatado = RegrasNotaMensalidade.Apelido(longo, "12345678000199", true, 40);
        Assert.Equal(40, desempatado.Length);
        Assert.EndsWith(" 0199", desempatado);
    }

    [Fact]
    public void Leitura_do_pagamento_usa_valor_e_data_do_pagamento()
    {
        var recebido = new DateTimeOffset(2026, 11, 9, 2, 0, 0, TimeSpan.Zero); // 08/11 23:00 em Brasília
        var (valor, dia) = NotasMensalidadeService.LerPagamento(
            """{"event":"PAYMENT_RECEIVED","payment":{"id":"pay_1","value":49.9,"paymentDate":"2026-11-07"}}""", recebido);
        Assert.Equal(49.90m, valor);
        Assert.Equal(new DateOnly(2026, 11, 7), dia);

        // Sem data no evento: o dia (de Brasília) em que o evento chegou.
        var (_, semData) = NotasMensalidadeService.LerPagamento("""{"payment":{"id":"pay_1","value":10}}""", recebido);
        Assert.Equal(new DateOnly(2026, 11, 8), semData);

        Assert.Null(NotasMensalidadeService.LerPagamento("""{"payment":{"id":"pay_1"}}""", recebido).Valor);
        Assert.Null(NotasMensalidadeService.LerPagamento("não é json", recebido).Valor);
    }

    [Fact]
    public void Configuracao_vazia_e_desligada()
    {
        Assert.False(new NotaMensalidadeOptions().Ligado);
        Assert.False(new NotaMensalidadeOptions { EmpresaId = Guid.NewGuid() }.Ligado);
        Assert.False(new NotaMensalidadeOptions { EmpresaId = Guid.Empty, ServicoId = Guid.NewGuid() }.Ligado);
        Assert.True(new NotaMensalidadeOptions { EmpresaId = Guid.NewGuid(), ServicoId = Guid.NewGuid() }.Ligado);
    }
}
