using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Avisos de cobrança por e-mail — atributos do Hangfire na interface (ver
/// ImportarIndicesEconomicosJob). <see cref="ExecutarAsync"/> é a varredura
/// diária; <see cref="ExecutarContaAsync"/> é disparado pelo webhook do
/// Asaas logo após um evento da conta. Os dois usam a mesma regra e a mesma
/// deduplicação, então nunca mandam o mesmo aviso duas vezes.
/// </summary>
public interface IAvisarCobrancaJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 2)]
    Task ExecutarAsync();

    [AutomaticRetry(Attempts = 3)]
    Task ExecutarContaAsync(Guid tenantId);
}

/// <summary>
/// Passa por toda conta ativa com assinatura em que a cobrança vale
/// (Cobranca:Ativa ou conta piloto, e não isenta), cada uma no PRÓPRIO escopo
/// de DI com o Tenant dela definido (mesmo padrão do
/// AvisarCertificadosVencendoJob). Com a cobrança desligada não faz nada.
/// A falha de uma conta não impede as outras.
/// </summary>
public sealed class AvisarCobrancaJob : IAvisarCobrancaJob
{
    public const string JobId = "avisar-cobranca";

    /// <summary>08:35 de Brasília — o aviso chega no começo do expediente.</summary>
    public const string Cron = "35 8 * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AvisarCobrancaJob> _logger;

    public AvisarCobrancaJob(IServiceScopeFactory scopeFactory, ILogger<AvisarCobrancaJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        await ReservarFundadoresAsync();

        List<Guid> contas;
        using (var scope = _scopeFactory.CreateScope())
        {
            var servico = scope.ServiceProvider.GetRequiredService<AvisosCobrancaService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            contas = (await db.Assinaturas.AsNoTracking()
                    .Where(a => db.Tenants.Any(t => t.Id == a.TenantId && t.Ativo))
                    .Select(a => a.TenantId)
                    .ToListAsync())
                .Where(servico.CobrancaValePara)
                .ToList();
        }

        var falhas = 0;
        foreach (var tenantId in contas)
        {
            try
            {
                await AvisarAsync(tenantId);
            }
            catch (Exception ex)
            {
                falhas++;
                _logger.LogError(ex, "Falha nos avisos de cobrança da conta {TenantId}.", tenantId);
            }
        }

        if (falhas > 0 && falhas == contas.Count)
            throw new InvalidOperationException($"Avisos de cobrança falharam em todas as {falhas} conta(s) — ver log.");
    }

    public Task ExecutarContaAsync(Guid tenantId) => AvisarAsync(tenantId);

    /// <summary>
    /// Guarda a vaga de fundador de quem confirmou o e-mail por um caminho
    /// que não reserva na hora (convite, senha redefinida, contas antigas).
    /// Falha aqui não impede os avisos.
    /// </summary>
    private async Task ReservarFundadoresAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var feitas = await scope.ServiceProvider.GetRequiredService<IReservaFundador>()
                .ReservarPendentesAsync(CancellationToken.None);
            if (feitas > 0)
                _logger.LogInformation("Varredura de fundador: {Quantidade} vaga(s) reservada(s).", feitas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na varredura de reservas de fundador.");
        }
    }

    private async Task AvisarAsync(Guid tenantId)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenantId);

        var resultado = await scope.ServiceProvider.GetRequiredService<IEnviarAvisosCobrancaUseCase>()
            .ExecutarAsync(tenantId, CancellationToken.None);

        if (resultado.Enviados.Count == 0 && resultado.Motivo == AvisosCobrancaService.MotivoSemDestinatario)
            _logger.LogWarning("Avisos de cobrança da conta {TenantId} não enviados: {Motivo}", tenantId, resultado.Motivo);
    }
}
