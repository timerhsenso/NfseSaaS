using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>Job diário do aviso de certificado vencendo — atributos do Hangfire na interface (ver ImportarIndicesEconomicosJob).</summary>
public interface IAvisarCertificadosVencendoJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 2)]
    Task ExecutarAsync();
}

/// <summary>
/// Passa por toda Empresa ativa de conta ativa, cada uma no PRÓPRIO escopo
/// de DI com o Tenant dela definido (mesmo padrão do
/// GerarReajustesAniversarioJob), e manda o aviso de 30/15/7 dias ou de
/// vencido quando for a hora (IAvisarCertificadoVencendoUseCase). A falha de
/// uma Empresa não impede as outras.
/// </summary>
public sealed class AvisarCertificadosVencendoJob : IAvisarCertificadosVencendoJob
{
    public const string JobId = "avisar-certificados-vencendo";

    /// <summary>08:20 de Brasília — o aviso chega no começo do expediente.</summary>
    public const string Cron = "20 8 * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AvisarCertificadosVencendoJob> _logger;

    public AvisarCertificadosVencendoJob(IServiceScopeFactory scopeFactory, ILogger<AvisarCertificadosVencendoJob> logger)
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
            empresas = (await db.Empresas.AsNoTracking().IgnoreQueryFilters()
                    .Where(e => e.Ativo && db.Tenants.Any(t => t.Id == e.TenantId && t.Ativo))
                    .Select(e => new { e.Id, e.TenantId })
                    .ToListAsync())
                .Select(x => (x.Id, x.TenantId))
                .ToList();
        }

        var hoje = DataBrasilia.Hoje();
        var falhas = 0;

        foreach (var (empresaId, tenantId) in empresas)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenantId);

                var resultado = await scope.ServiceProvider.GetRequiredService<IAvisarCertificadoVencendoUseCase>()
                    .ExecutarAsync(empresaId, hoje, CancellationToken.None);

                if (resultado.Enviado)
                    _logger.LogInformation(
                        "Aviso de certificado da Empresa {EmpresaId} ({DiasRestantes} dias, faixa {Faixa}) enviado para {Destinatarios}.",
                        empresaId, resultado.DiasRestantes, resultado.Faixa, string.Join(", ", resultado.Destinatarios));
                else if (resultado.Faixa is not null)
                    _logger.LogInformation("Aviso de certificado da Empresa {EmpresaId} não enviado: {Motivo}", empresaId, resultado.Motivo);
            }
            catch (Exception ex)
            {
                falhas++;
                _logger.LogError(ex, "Falha ao verificar o certificado da Empresa {EmpresaId}.", empresaId);
            }
        }

        if (falhas > 0 && falhas == empresas.Count)
            throw new InvalidOperationException($"Aviso de certificado falhou em todas as {falhas} empresa(s) — ver log.");
    }
}
