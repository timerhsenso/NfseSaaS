using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.DadosConta;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Exportação dos dados da conta (D1) — atributos do Hangfire na interface
/// (ver ImportarIndicesEconomicosJob).
/// - Gerar: disparado pelo pedido na tela "Meus dados"; uma montagem por vez
///   no servidor inteiro (poupa a VPS) e sem retry automático (a falha vira
///   Erro na tela, a conta pede de novo, e o operador recebe o alerta de job).
/// - Limpar: diária, apaga o que expirou.
/// </summary>
public interface IExportacaoDadosJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    Task GerarAsync(Guid tenantId, Guid exportacaoId);

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 2)]
    Task LimparAsync();
}

public sealed class ExportacaoDadosJob : IExportacaoDadosJob
{
    public const string JobIdLimpeza = "limpar-exportacoes-dados";

    /// <summary>04:30 de Brasília.</summary>
    public const string CronLimpeza = "30 4 * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExportacaoDadosJob> _logger;

    public ExportacaoDadosJob(IServiceScopeFactory scopeFactory, ILogger<ExportacaoDadosJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task GerarAsync(Guid tenantId, Guid exportacaoId)
    {
        // Escopo próprio com a conta definida: o filtro de conta vale em todas as consultas.
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenantId);
        await scope.ServiceProvider.GetRequiredService<IGerarExportacaoDadosUseCase>().GerarAsync(exportacaoId, CancellationToken.None);
    }

    public async Task LimparAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var resultado = await scope.ServiceProvider.GetRequiredService<IGerarExportacaoDadosUseCase>().LimparAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        _logger.LogDebug("Limpeza das exportações: {Expiradas} expirada(s), {Apagados} arquivo(s).", resultado.Expiradas, resultado.ArquivosApagados);
    }
}
