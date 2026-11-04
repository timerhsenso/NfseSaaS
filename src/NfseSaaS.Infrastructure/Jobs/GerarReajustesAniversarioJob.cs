using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>Job diário dos reajustes de aniversário — atributos do Hangfire na interface (ver ImportarIndicesEconomicosJob).</summary>
public interface IGerarReajustesAniversarioJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 2)]
    Task ExecutarAsync();
}

/// <summary>
/// Roda Empresa por Empresa, cada uma no PRÓPRIO escopo de DI com o Tenant
/// dela definido (mesmo padrão do ExecutarAutomacaoNotaMensalJob): leitura e
/// gravação passam pelo isolamento de tenant normal, e a falha de uma
/// Empresa não impede as outras.
/// </summary>
public sealed class GerarReajustesAniversarioJob : IGerarReajustesAniversarioJob
{
    public const string JobId = "gerar-reajustes-aniversario";

    /// <summary>10:10 de Brasília — depois da importação de índices (09:40), para já usar o índice do dia.</summary>
    public const string Cron = "10 10 * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GerarReajustesAniversarioJob> _logger;

    public GerarReajustesAniversarioJob(IServiceScopeFactory scopeFactory, ILogger<GerarReajustesAniversarioJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        List<(Guid EmpresaId, Guid TenantId)> empresas;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Sem Tenant resolvido aqui: descobre quais Empresas têm Contrato
            // reajustável por índice, em todos os tenants.
            empresas = (await db.Contratos.AsNoTracking().IgnoreQueryFilters()
                    .Where(c => c.Status == StatusContrato.Ativo
                        && c.IndiceReajuste != null && c.IndiceReajuste != IndiceReajusteContrato.Outro
                        // Conta suspensa pelo operador da plataforma fica de fora.
                        && db.Tenants.Any(t => t.Id == c.TenantId && t.Ativo))
                    .Select(c => new { c.EmpresaId, c.TenantId })
                    .Distinct()
                    .ToListAsync())
                .Select(x => (x.EmpresaId, x.TenantId))
                .ToList();
        }

        var falhas = 0;
        foreach (var (empresaId, tenantId) in empresas)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenantId);

                var resultado = await scope.ServiceProvider.GetRequiredService<IGerarReajustesAniversarioUseCase>()
                    .ExecutarAsync(empresaId, CancellationToken.None);

                if (resultado.Itens.Count > 0)
                    _logger.LogInformation(
                        "Reajustes de aniversário da Empresa {EmpresaId}: {Resumo}", empresaId,
                        string.Join("; ", resultado.Itens.Select(i => $"{i.Descricao} {i.Aniversario:yyyy-MM-dd} {i.Resultado}")));

                // Aguardando aprovação / aplicados: e-mail aos administradores
                // da conta. Falha no aviso não desfaz os reajustes já gravados.
                try
                {
                    var avisados = await scope.ServiceProvider.GetRequiredService<AvisoReajustesAniversario>()
                        .AvisarAsync(empresaId, resultado.Itens, CancellationToken.None);
                    if (avisados.Count > 0)
                        _logger.LogInformation("Aviso de reajustes da Empresa {EmpresaId} enviado para {Destinatarios}.", empresaId, string.Join(", ", avisados));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reajustes da Empresa {EmpresaId} gerados, mas o e-mail de aviso não pôde ser enfileirado.", empresaId);
                }
            }
            catch (Exception ex)
            {
                falhas++;
                _logger.LogError(ex, "Falha ao gerar reajustes de aniversário da Empresa {EmpresaId}.", empresaId);
            }
        }

        if (falhas > 0 && falhas == empresas.Count)
            throw new InvalidOperationException($"Reajustes de aniversário falharam em todas as {falhas} empresa(s) — ver log.");
    }
}
