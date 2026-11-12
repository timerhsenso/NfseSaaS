using System.Globalization;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

/// <summary>
/// Regras para desfazer um reajuste registrado por engano (ex.: feito antes
/// de sair o índice do mês) e para o aviso de "dois reajustes em pouco
/// tempo". Puras — sem banco — para valerem igual na tela, na API e nos
/// testes.
///
/// O histórico é uma corrente: cada evento parte do valor do anterior. Por
/// isso só o ÚLTIMO evento aplicado pode ser desfeito, e só se for um
/// reajuste sem nota de produção emitida com o valor novo (a nota fiscal já
/// existe com aquele valor — aí a correção é um novo reajuste). O evento
/// desfeito não some: fica no histórico como Desfeito, com o motivo.
/// </summary>
public static class RegrasDesfazerReajuste
{
    public const int TamanhoMinimoMotivo = 10;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Quando o evento passou a valer no Contrato: a aprovação (reajuste pendente) ou o registro.</summary>
    public static DateTimeOffset AplicadoEm(MovimentacaoValorContrato evento) => evento.DecididoEm ?? evento.CreatedAt;

    /// <summary>O reajuste aplicado mais recente do Contrato (candidato a desfazer), ou null.</summary>
    public static MovimentacaoValorContrato? UltimoReajusteAplicado(IEnumerable<MovimentacaoValorContrato> eventos) =>
        eventos
            .Where(e => e.Tipo == TipoMovimentacaoValor.Reajuste && e.Status == StatusMovimentacaoValor.Aplicado)
            .OrderByDescending(AplicadoEm)
            .FirstOrDefault();

    /// <summary>
    /// Por que o reajuste NÃO pode ser desfeito (texto para a tela), ou null
    /// se pode. <paramref name="notaProducaoDepois"/> = data da primeira nota
    /// de produção do Contrato emitida depois do reajuste (null se não há).
    /// </summary>
    public static string? MotivoBloqueio(
        MovimentacaoValorContrato reajuste,
        IReadOnlyCollection<MovimentacaoValorContrato> eventos,
        decimal valorAtualContrato,
        DateTimeOffset? notaProducaoDepois)
    {
        if (reajuste.Tipo != TipoMovimentacaoValor.Reajuste)
            return "Só reajustes podem ser desfeitos. Uma alteração de escopo se corrige editando os serviços do contrato.";

        if (reajuste.Status != StatusMovimentacaoValor.Aplicado)
            return reajuste.Status == StatusMovimentacaoValor.Desfeito
                ? "Este reajuste já foi desfeito."
                : "Só um reajuste aplicado pode ser desfeito.";

        if (eventos.Any(e => e.Status == StatusMovimentacaoValor.Pendente))
            return "Há um reajuste aguardando aprovação. Decida esse reajuste antes de desfazer o anterior.";

        var aplicadoEm = AplicadoEm(reajuste);
        var posterior = eventos
            .Where(e => e.Id != reajuste.Id
                && e.Tipo != TipoMovimentacaoValor.ValorInicial
                && e.Status is StatusMovimentacaoValor.Aplicado or StatusMovimentacaoValor.Recusado
                && AplicadoEm(e) > aplicadoEm)
            .OrderBy(AplicadoEm)
            .FirstOrDefault();
        if (posterior is not null)
            return $"Só o último evento do histórico pode ser desfeito. Depois deste reajuste veio {Descrever(posterior)} " +
                $"com vigência em {Data(posterior.DataVigencia)}.";

        if (valorAtualContrato != reajuste.ValorNovo)
            return $"O valor do contrato mudou depois deste reajuste (agora {Reais(valorAtualContrato)}). " +
                "Para corrigir, registre um novo reajuste a partir do valor atual.";

        if (notaProducaoDepois is { } dataNota)
            return $"Já saiu nota de produção com o valor reajustado ({Data(DateOnly.FromDateTime(dataNota.UtcDateTime.AddHours(-3)))}). " +
                "Para corrigir, registre um novo reajuste a partir do valor atual.";

        return null;
    }

    /// <summary>Motivo limpo, ou a mensagem de erro (obrigatório, de 10 a 500 caracteres).</summary>
    public static (string? Motivo, string? Erro) ValidarMotivo(string? motivo)
    {
        var limpo = motivo?.Trim();
        if (string.IsNullOrEmpty(limpo) || limpo.Length < TamanhoMinimoMotivo)
            return (null, $"Explique por que o reajuste está sendo desfeito (mínimo {TamanhoMinimoMotivo} caracteres).");
        if (limpo.Length > MovimentacaoValorContrato.TamanhoMaximoMotivo)
            return (null, $"O motivo pode ter no máximo {MovimentacaoValorContrato.TamanhoMaximoMotivo} caracteres.");
        return (limpo, null);
    }

    /// <summary>
    /// Para onde volta a "data do último reajuste" do Contrato. Eventos novos
    /// guardam o valor de antes (DataUltimoReajusteAnterior). Nos antigos,
    /// sem esse campo: a vigência do reajuste anterior que ainda vale, ou o
    /// aniversário anterior (vigência − periodicidade) se cair dentro do
    /// contrato, ou nenhuma (o ciclo volta a contar do início do contrato).
    /// </summary>
    public static DateOnly? DataUltimoReajusteAoDesfazer(
        MovimentacaoValorContrato reajuste,
        IEnumerable<MovimentacaoValorContrato> eventos,
        int periodicidadeMeses,
        DateOnly dataInicioContrato)
    {
        if (reajuste.DataUltimoReajusteAnterior is { } guardada)
            return guardada;

        var aplicadoEm = AplicadoEm(reajuste);
        var anterior = eventos
            .Where(e => e.Id != reajuste.Id
                && e.Tipo == TipoMovimentacaoValor.Reajuste
                && e.Status is StatusMovimentacaoValor.Aplicado or StatusMovimentacaoValor.Recusado
                && AplicadoEm(e) < aplicadoEm)
            .OrderByDescending(AplicadoEm)
            .FirstOrDefault();
        if (anterior is not null)
            return anterior.DataVigencia;

        var aniversarioAnterior = reajuste.DataVigencia.AddMonths(-Math.Max(1, periodicidadeMeses));
        return aniversarioAnterior >= dataInicioContrato ? aniversarioAnterior : null;
    }

    /// <summary>
    /// Reajuste aplicado há menos de uma periodicidade (12 meses, no comum)
    /// da data do novo — o aviso "dois reajustes em tão pouco tempo?".
    /// Reajuste de contrato é anual: um segundo dentro do ciclo quase sempre
    /// é engano ou correção.
    /// </summary>
    public static MovimentacaoValorContrato? ReajusteRecente(
        IEnumerable<MovimentacaoValorContrato> eventos, DateOnly dataNovoReajuste, int periodicidadeMeses)
    {
        var limite = dataNovoReajuste.AddMonths(-PeriodicidadeEfetiva(periodicidadeMeses));
        return eventos
            .Where(e => e.Tipo == TipoMovimentacaoValor.Reajuste
                && e.Status == StatusMovimentacaoValor.Aplicado
                && e.DataVigencia > limite)
            .OrderByDescending(AplicadoEm)
            .FirstOrDefault();
    }

    /// <summary>Contrato sem periodicidade (avulso) usa 12 meses no aviso.</summary>
    public static int PeriodicidadeEfetiva(int periodicidadeMeses) => periodicidadeMeses > 0 ? periodicidadeMeses : 12;

    public static string MensagemReajusteRecente(MovimentacaoValorContrato recente, int periodicidadeMeses) =>
        $"Este contrato já teve reajuste em {Data(recente.DataVigencia)} ({Reais(recente.ValorAnterior)} → {Reais(recente.ValorNovo)}), " +
        $"há menos de {PeriodicidadeEfetiva(periodicidadeMeses)} meses. Confirme se é mesmo mais um reajuste, ou substitua o anterior.";

    private static string Descrever(MovimentacaoValorContrato e) => e.Tipo switch
    {
        TipoMovimentacaoValor.Alteracao => "uma alteração de escopo",
        _ when e.Status == StatusMovimentacaoValor.Recusado => "um reajuste não aplicado",
        _ => "outro reajuste"
    };

    private static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", PtBr);

    private static string Reais(decimal? v) => v is null ? "—" : "R$ " + v.Value.ToString("N2", PtBr);
}
