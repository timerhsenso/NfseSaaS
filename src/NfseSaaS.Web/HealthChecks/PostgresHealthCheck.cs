using Microsoft.Extensions.Diagnostics.HealthChecks;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Web.HealthChecks;

/// <summary>
/// Health check simples do PostgreSQL: tenta abrir conexão via o próprio
/// AppDbContext (Database.CanConnectAsync), sem pacote NuGet adicional.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly AppDbContext _dbContext;

    public PostgresHealthCheck(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var podeConectar = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return podeConectar
                ? HealthCheckResult.Healthy("PostgreSQL acessível.")
                : HealthCheckResult.Unhealthy("Não foi possível conectar ao PostgreSQL.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Erro ao conectar ao PostgreSQL.", ex);
        }
    }
}
