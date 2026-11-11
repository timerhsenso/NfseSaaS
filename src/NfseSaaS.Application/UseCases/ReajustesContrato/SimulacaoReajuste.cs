using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

/// <summary>
/// "Quanto fica o reajuste pelo índice?" na tela do Histórico de valores,
/// antes de registrar. Mesma conta do reajuste automático do aniversário
/// (acumulado de 12 meses publicados, regra do índice negativo, só a parte
/// fixa do contrato) — o manual e o automático nunca dão números diferentes.
/// Não grava nada.
/// </summary>
public interface ISimularReajusteContratoUseCase
{
    Task<SimulacaoReajusteResponse> ExecutarAsync(Guid contratoId, SimularReajusteRequest request, CancellationToken cancellationToken);
}

/// <param name="Indice">Nulo = o índice do contrato.</param>
/// <param name="DataReajuste">Nula = a data sugerida (aniversário próximo ou vencido; senão hoje).</param>
/// <param name="AplicarNegativo">Nulo = a regra do contrato (AplicarIndiceNegativo); true/false = escolha da tela.</param>
public sealed record SimularReajusteRequest(
    IndiceReajusteContrato? Indice,
    DateOnly? DataReajuste,
    bool? AplicarNegativo);

/// <param name="DataSugerida">Data que a tela propõe para o reajuste (aniversário ou hoje).</param>
/// <param name="Aniversario">Próximo aniversário de reajuste do contrato (nulo sem periodicidade).</param>
/// <param name="Disponivel">False quando não dá para calcular (sem índice, "Outro", mês faltando, índice parado) — <paramref name="Mensagem"/> diz por quê.</param>
/// <param name="PercentualIndice">Acumulado do índice (pode ser negativo).</param>
/// <param name="PercentualAplicado">O que vale de fato: 0 quando o acumulado é negativo e não se reduz.</param>
/// <param name="NegativoMantido">Acumulado negativo e o valor fica igual (regra do contrato ou escolha da tela).</param>
/// <param name="ContratoReduzComNegativo">Regra cadastrada no contrato (para a tela dizer "conforme o contrato").</param>
/// <param name="ParteVariavel">Valor das linhas variáveis, que ficam fora do reajuste.</param>
/// <param name="ServicosForaDoReajuste">Descrição das linhas variáveis (fora do reajuste).</param>
public sealed record SimulacaoReajusteResponse(
    IndiceReajusteContrato? Indice,
    DateOnly DataReajuste,
    DateOnly DataSugerida,
    DateOnly? Aniversario,
    bool Disponivel,
    string? Mensagem,
    DateOnly? CompetenciaInicio,
    DateOnly? CompetenciaFim,
    decimal? PercentualIndice,
    decimal? PercentualAplicado,
    bool NegativoMantido,
    bool ContratoReduzComNegativo,
    decimal ValorAtual,
    decimal ParteVariavel,
    decimal? ValorNovo,
    IReadOnlyList<string> ServicosForaDoReajuste,
    IReadOnlyList<IndiceAcumuladoMesResponse> Meses);

/// <summary>Data que a tela propõe para o reajuste.</summary>
public static class DataSugeridaReajuste
{
    /// <summary>Aniversário a até tantos dias pela frente já é proposto (reajuste combinado com antecedência).</summary>
    public const int DiasAntecedencia = 45;

    /// <summary>
    /// Aniversário vencido ou chegando (até <see cref="DiasAntecedencia"/>
    /// dias) → o aniversário (é a data certa da vigência, mesmo atrasado);
    /// aniversário distante ou contrato sem periodicidade → hoje.
    /// </summary>
    public static DateOnly Calcular(DateOnly? aniversario, DateOnly hoje) =>
        aniversario is { } data && data <= hoje.AddDays(DiasAntecedencia) ? data : hoje;
}
