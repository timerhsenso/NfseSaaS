using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>Conciliação diária com o Asaas — atributos do Hangfire na interface (ver ImportarIndicesEconomicosJob).</summary>
public interface IConciliarCobrancaJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 2)]
    Task ExecutarAsync();
}

/// <summary>
/// Passa por toda assinatura com id no Asaas que não está cancelada, cada
/// uma no PRÓPRIO escopo de DI com o Tenant dela (mesmo padrão do
/// AvisarCobrancaJob), e corrige o que o webhook deixou passar. Roda antes
/// dos avisos de cobrança, para eles saírem com a situação já certa. Sem a
/// chave do Asaas, não faz nada. A falha de uma conta não impede as outras.
/// </summary>
public sealed class ConciliarCobrancaJob : IConciliarCobrancaJob
{
    public const string JobId = "conciliar-cobranca";

    /// <summary>07:50 de Brasília — antes dos avisos de cobrança (08:35).</summary>
    public const string Cron = "50 7 * * *";

    /// <summary>Pausa entre contas, para não estourar o limite de requisições do Asaas.</summary>
    private static readonly TimeSpan PausaEntreContas = TimeSpan.FromMilliseconds(300);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConciliarCobrancaJob> _logger;

    public ConciliarCobrancaJob(IServiceScopeFactory scopeFactory, ILogger<ConciliarCobrancaJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        List<Guid> contas;
        using (var scope = _scopeFactory.CreateScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<IGatewayCobranca>().Configurado)
            {
                _logger.LogInformation("Conciliação com o Asaas: chave não configurada, nada a fazer.");
                return;
            }

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            contas = await db.Assinaturas.AsNoTracking()
                .Where(a => a.AsaasAssinaturaId != null && a.Status != StatusAssinatura.Cancelada)
                .Select(a => a.TenantId)
                .ToListAsync();
        }

        int corrigidas = 0, falhas = 0;
        foreach (var tenantId in contas)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenantId);
                var resultado = await scope.ServiceProvider.GetRequiredService<IConciliarCobrancaUseCase>()
                    .ConciliarAsync(tenantId, operadorId: null, CancellationToken.None);
                if (resultado.Mudou)
                    corrigidas++;
            }
            catch (Exception ex)
            {
                falhas++;
                _logger.LogError(ex, "Falha na conciliação com o Asaas da conta {TenantId}.", tenantId);
            }
            await Task.Delay(PausaEntreContas);
        }

        _logger.LogInformation("Conciliação com o Asaas: {Total} conta(s) conferida(s), {Corrigidas} corrigida(s), {Falhas} falha(s).",
            contas.Count, corrigidas, falhas);

        if (falhas > 0 && falhas == contas.Count)
            throw new InvalidOperationException($"Conciliação com o Asaas falhou em todas as {falhas} conta(s) — ver log.");
    }
}
