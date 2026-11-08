using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Legal;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>Aviso prévio dos novos Termos/Política — atributos do Hangfire na interface (ver ImportarIndicesEconomicosJob).</summary>
public interface IAvisarNovosTermosJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 2)]
    Task ExecutarAsync();
}

/// <summary>
/// Diário, mas só faz alguma coisa enquanto a versão nova ainda não vale
/// (DocumentosLegais.VigentesDesde no futuro): manda o aviso a quem ainda
/// não recebeu (conta que surgiu depois, e-mail confirmado depois). Cada
/// conta no PRÓPRIO escopo de DI com o Tenant dela; a falha de uma não
/// impede as outras.
/// </summary>
public sealed class AvisarNovosTermosJob : IAvisarNovosTermosJob
{
    public const string JobId = "avisar-novos-termos";

    /// <summary>09:10 de Brasília.</summary>
    public const string Cron = "10 9 * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AvisarNovosTermosJob> _logger;

    public AvisarNovosTermosJob(IServiceScopeFactory scopeFactory, ILogger<AvisarNovosTermosJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        if (DocumentosLegais.VersaoAtualVigente(DataBrasilia.Hoje()))
            return;

        List<Guid> contas;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            contas = await db.Tenants.AsNoTracking().Where(t => t.Ativo).Select(t => t.Id).ToListAsync();
        }

        int enviados = 0, falhas = 0;
        foreach (var tenantId in contas)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenantId);
                enviados += await scope.ServiceProvider.GetRequiredService<AvisoNovosTermosService>().AvisarContaAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                falhas++;
                _logger.LogError(ex, "Falha no aviso de novos termos da conta {TenantId}.", tenantId);
            }
        }

        if (enviados > 0)
            _logger.LogInformation("Aviso dos Termos/Política {Versao}: {Enviados} e-mail(s) na fila.", DocumentosLegais.VersaoTermosDeUso, enviados);

        if (falhas > 0 && falhas == contas.Count)
            throw new InvalidOperationException($"Aviso de novos termos falhou em todas as {falhas} conta(s) — ver log.");
    }
}
