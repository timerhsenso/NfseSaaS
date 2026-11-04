using Hangfire;
using NfseSaaS.Application.UseCases.IndicesEconomicos;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Job diário da importação de índices. Os atributos do Hangfire ficam na
/// INTERFACE porque é ela que o RecurringJob registra (AddOrUpdate&lt;I&gt;).
/// </summary>
public interface IImportarIndicesEconomicosJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    Task ExecutarAsync();
}

public sealed class ImportarIndicesEconomicosJob : IImportarIndicesEconomicosJob
{
    public const string JobId = "importar-indices-economicos";

    /// <summary>
    /// 09:40 de Brasília, todo dia: o IBGE divulga o IPCA/INPC às 9h e a FGV
    /// o IGP-M/INCC às 8h; quando não há divulgação no dia, a importação só
    /// confirma o que já existe (6 requisições pequenas).
    /// </summary>
    public const string Cron = "40 9 * * *";

    private readonly IImportarIndicesEconomicosUseCase _importar;

    public ImportarIndicesEconomicosJob(IImportarIndicesEconomicosUseCase importar)
    {
        _importar = importar;
    }

    public async Task ExecutarAsync()
    {
        var resultado = await _importar.ExecutarAsync(CancellationToken.None);

        // Falha parcial (um índice) fica no log e na tela; falha TOTAL quase
        // sempre é rede/Banco Central fora — lança para o Hangfire marcar o
        // job como falho e tentar de novo.
        if (resultado.TodosFalharam)
            throw new InvalidOperationException(
                "Nenhum índice foi importado do Banco Central: " +
                string.Join(" | ", resultado.Indices.Select(i => $"{i.Indice}: {i.Erro}")));
    }
}
