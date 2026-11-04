using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.IndicesEconomicos;

/// <summary>Índices oferecidos na tela (todos os do enum menos "Outro"), com a última competência já carregada.</summary>
public interface IListarIndicesDisponiveisUseCase
{
    Task<IReadOnlyList<IndiceDisponivelResponse>> ExecutarAsync(CancellationToken cancellationToken);
}

/// <summary>Variações mensais de um índice, da mais recente para a mais antiga.</summary>
public interface IListarIndicesEconomicosUseCase
{
    Task<IReadOnlyList<IndiceEconomicoMensalResponse>> ExecutarAsync(IndiceReajusteContrato indice, CancellationToken cancellationToken);
}

/// <summary>
/// Acumulado de 12 meses do índice para uma data de aniversário — regra em
/// <see cref="CalculadoraIndiceAcumulado"/>. É o que o reajuste automático
/// do Contrato vai usar.
/// </summary>
public interface ICalcularIndiceAcumuladoUseCase
{
    Task<IndiceAcumuladoResponse> ExecutarAsync(IndiceReajusteContrato indice, DateOnly dataReferencia, CancellationToken cancellationToken);
}

/// <summary>Lança ou corrige um mês à mão (operador da plataforma). Vira Fonte=Manual e a importação não toca mais.</summary>
public interface ISalvarIndiceManualUseCase
{
    Task ExecutarAsync(SalvarIndiceManualRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Remove um mês (operador da plataforma). Para um valor Manual, é o jeito
/// de "devolver" o mês ao Banco Central: a próxima importação o traz de volta.
/// </summary>
public interface IExcluirIndiceEconomicoUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Busca no Banco Central os meses que faltam (24 meses na primeira vez) e
/// atualiza os recentes. Chamado pelo job diário e pelo botão "Importar agora".
/// </summary>
public interface IImportarIndicesEconomicosUseCase
{
    Task<ImportacaoIndicesResponse> ExecutarAsync(CancellationToken cancellationToken);
}

/// <param name="Competencia">Qualquer dia do mês — é normalizado para o dia 1.</param>
/// <param name="Variacao">Em percentual (0,56 = 0,56%).</param>
public sealed record SalvarIndiceManualRequest(IndiceReajusteContrato Indice, DateOnly Competencia, decimal Variacao);
