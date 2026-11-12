using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Snapshots;
using NfseSaaS.Infrastructure.Contratos;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Desfazer o último reajuste e o aviso de dois reajustes no mesmo ciclo.</summary>
public sealed class RegrasDesfazerReajusteTests
{
    private static readonly DateTimeOffset Ontem = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    private static MovimentacaoValorContrato Evento(
        TipoMovimentacaoValor tipo, DateOnly vigencia, decimal? anterior, decimal novo, DateTimeOffset criadoEm,
        StatusMovimentacaoValor status = StatusMovimentacaoValor.Aplicado) =>
        new()
        {
            Tipo = tipo, Status = status, DataVigencia = vigencia,
            ValorAnterior = anterior, ValorNovo = novo, CreatedAt = criadoEm
        };

    private static (MovimentacaoValorContrato Inicial, MovimentacaoValorContrato Reajuste) CasoDoCarlos()
    {
        var inicial = Evento(TipoMovimentacaoValor.ValorInicial, new DateOnly(2025, 11, 11), null, 328.98m, Ontem.AddYears(-1));
        var reajuste = Evento(TipoMovimentacaoValor.Reajuste, new DateOnly(2026, 11, 11), 328.98m, 344.03m, Ontem);
        return (inicial, reajuste);
    }

    [Fact]
    public void Ultimo_reajuste_aplicado_e_desfazivel_sem_nota_e_sem_evento_depois()
    {
        var (inicial, reajuste) = CasoDoCarlos();
        var eventos = new[] { inicial, reajuste };

        Assert.Same(reajuste, RegrasDesfazerReajuste.UltimoReajusteAplicado(eventos));
        Assert.Null(RegrasDesfazerReajuste.MotivoBloqueio(reajuste, eventos, 344.03m, null));
    }

    [Fact]
    public void Nota_de_producao_depois_do_reajuste_bloqueia()
    {
        var (inicial, reajuste) = CasoDoCarlos();

        var bloqueio = RegrasDesfazerReajuste.MotivoBloqueio(reajuste, new[] { inicial, reajuste }, 344.03m, Ontem.AddHours(2));

        Assert.NotNull(bloqueio);
        Assert.Contains("nota de produção", bloqueio);
        Assert.Contains("11/11/2026", bloqueio);
    }

    [Fact]
    public void Evento_depois_do_reajuste_bloqueia_so_o_ultimo_pode()
    {
        var (inicial, reajuste) = CasoDoCarlos();
        var alteracao = Evento(TipoMovimentacaoValor.Alteracao, new DateOnly(2026, 11, 12), 344.03m, 400m, Ontem.AddDays(1));
        var eventos = new[] { inicial, reajuste, alteracao };

        var bloqueio = RegrasDesfazerReajuste.MotivoBloqueio(reajuste, eventos, 400m, null);

        Assert.Contains("Só o último evento", bloqueio);
        Assert.Contains("alteração de escopo", bloqueio);
    }

    [Fact]
    public void Pendente_valor_mudado_e_ja_desfeito_bloqueiam()
    {
        var (inicial, reajuste) = CasoDoCarlos();
        var pendente = Evento(TipoMovimentacaoValor.Reajuste, new DateOnly(2027, 11, 11), 344.03m, 360m, Ontem.AddDays(1), StatusMovimentacaoValor.Pendente);

        Assert.Contains("aguardando aprovação",
            RegrasDesfazerReajuste.MotivoBloqueio(reajuste, new[] { inicial, reajuste, pendente }, 344.03m, null));
        Assert.Contains("valor do contrato mudou",
            RegrasDesfazerReajuste.MotivoBloqueio(reajuste, new[] { inicial, reajuste }, 350m, null));

        reajuste.Status = StatusMovimentacaoValor.Desfeito;
        Assert.Contains("já foi desfeito", RegrasDesfazerReajuste.MotivoBloqueio(reajuste, new[] { inicial, reajuste }, 328.98m, null));
        Assert.Null(RegrasDesfazerReajuste.UltimoReajusteAplicado(new[] { inicial, reajuste }));
    }

    [Fact]
    public void Reajuste_aprovado_conta_pela_data_da_aprovacao()
    {
        var inicial = Evento(TipoMovimentacaoValor.ValorInicial, new DateOnly(2025, 11, 11), null, 100m, Ontem.AddYears(-1));
        // Proposto antes da alteração, aprovado depois dela: é o último.
        var reajuste = Evento(TipoMovimentacaoValor.Reajuste, new DateOnly(2026, 11, 11), 120m, 125m, Ontem.AddDays(-5));
        reajuste.DecididoEm = Ontem.AddDays(1);
        var alteracao = Evento(TipoMovimentacaoValor.Alteracao, new DateOnly(2026, 11, 1), 100m, 120m, Ontem);
        var eventos = new[] { inicial, reajuste, alteracao };

        Assert.Same(reajuste, RegrasDesfazerReajuste.UltimoReajusteAplicado(eventos));
        Assert.Null(RegrasDesfazerReajuste.MotivoBloqueio(reajuste, eventos, 125m, null));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("curto", false)]
    [InlineData("   errado   ", false)]
    [InlineData("índice de outubro saiu hoje", true)]
    public void Motivo_obrigatorio_com_minimo_de_10_caracteres(string? motivo, bool valido)
    {
        var (limpo, erro) = RegrasDesfazerReajuste.ValidarMotivo(motivo);
        Assert.Equal(valido, erro is null);
        Assert.Equal(valido, limpo is not null);
    }

    [Fact]
    public void Data_do_ultimo_reajuste_volta_para_a_guardada_ou_a_do_reajuste_anterior_ou_o_aniversario_anterior()
    {
        var (inicial, reajuste) = CasoDoCarlos();
        var inicio = new DateOnly(2025, 11, 11);

        // Evento novo: guardou a data de antes.
        reajuste.DataUltimoReajusteAnterior = new DateOnly(2025, 11, 11);
        Assert.Equal(new DateOnly(2025, 11, 11),
            RegrasDesfazerReajuste.DataUltimoReajusteAoDesfazer(reajuste, new[] { inicial, reajuste }, 12, inicio));

        // Evento antigo, sem o campo: aniversário anterior, dentro do contrato.
        reajuste.DataUltimoReajusteAnterior = null;
        Assert.Equal(new DateOnly(2025, 11, 11),
            RegrasDesfazerReajuste.DataUltimoReajusteAoDesfazer(reajuste, new[] { inicial, reajuste }, 12, inicio));

        // ...antes do início do contrato: nenhuma (conta do início).
        Assert.Null(RegrasDesfazerReajuste.DataUltimoReajusteAoDesfazer(reajuste, new[] { inicial, reajuste }, 12, new DateOnly(2026, 1, 1)));

        // ...com um reajuste anterior que continua valendo: a vigência dele.
        var anterior = Evento(TipoMovimentacaoValor.Reajuste, new DateOnly(2025, 12, 1), 300m, 328.98m, Ontem.AddMonths(-11));
        Assert.Equal(new DateOnly(2025, 12, 1),
            RegrasDesfazerReajuste.DataUltimoReajusteAoDesfazer(reajuste, new[] { inicial, anterior, reajuste }, 12, inicio));
    }

    [Fact]
    public void Reajuste_recente_e_o_aplicado_dentro_da_periodicidade()
    {
        var (inicial, reajuste) = CasoDoCarlos();
        var eventos = new[] { inicial, reajuste };

        Assert.Same(reajuste, RegrasDesfazerReajuste.ReajusteRecente(eventos, new DateOnly(2026, 11, 12), 12));
        Assert.Same(reajuste, RegrasDesfazerReajuste.ReajusteRecente(eventos, new DateOnly(2027, 11, 10), 12));
        Assert.Null(RegrasDesfazerReajuste.ReajusteRecente(eventos, new DateOnly(2027, 11, 11), 12));
        // Avulso (sem periodicidade): 12 meses.
        Assert.Same(reajuste, RegrasDesfazerReajuste.ReajusteRecente(eventos, new DateOnly(2027, 6, 1), 0));

        reajuste.Status = StatusMovimentacaoValor.Desfeito;
        Assert.Null(RegrasDesfazerReajuste.ReajusteRecente(eventos, new DateOnly(2026, 11, 12), 12));

        reajuste.Status = StatusMovimentacaoValor.Aplicado;
        var mensagem = RegrasDesfazerReajuste.MensagemReajusteRecente(reajuste, 12);
        Assert.Contains("11/11/2026", mensagem);
        Assert.Contains("R$ 328,98 → R$ 344,03", mensagem);
    }

    [Fact]
    public void Restaura_linhas_pela_composicao_anterior_ou_pelo_fator_inverso()
    {
        var manutencao = Guid.NewGuid();
        var helpDesk = Guid.NewGuid();

        // Com a composição de antes: cópia exata.
        var linhas = new List<ContratoServico>
        {
            new() { ServicoId = manutencao, Quantidade = 1, ValorUnitario = 1126.83m },
            new() { ServicoId = helpDesk, Quantidade = 1, ValorUnitario = 500m, ValorVariavel = true }
        };
        DesfazerReajusteContrato.RestaurarLinhas(linhas,
            new[] { new ComposicaoContratoLinha(manutencao, "Manutenção", 1, 1000m), new ComposicaoContratoLinha(helpDesk, "HelpDesk", 1, 500m) },
            1626.83m, 1500m);
        Assert.Equal(1000m, linhas[0].ValorUnitario);
        Assert.Equal(500m, linhas[1].ValorUnitario);

        // Sem composição (evento antigo): fator inverso só na parte fixa.
        linhas[0].ValorUnitario = 1126.83m;
        DesfazerReajusteContrato.RestaurarLinhas(linhas, Array.Empty<ComposicaoContratoLinha>(), 1626.83m, 1500m);
        Assert.Equal(1000m, linhas[0].ValorUnitario);
        Assert.Equal(500m, linhas[1].ValorUnitario);
    }
}
