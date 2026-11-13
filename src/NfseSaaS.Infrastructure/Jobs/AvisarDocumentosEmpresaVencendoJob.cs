using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>DOC1: job diário do aviso de documento da Empresa vencendo — atributos do Hangfire na interface.</summary>
public interface IAvisarDocumentosEmpresaVencendoJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 2)]
    Task ExecutarAsync();
}

/// <summary>
/// Mesmo padrão do AvisarCertificadosVencendoJob: só Empresas ativas de
/// conta ativa que TÊM documento com validade, cada uma no próprio escopo
/// com o Tenant definido. A falha de uma Empresa não impede as outras.
/// </summary>
public sealed class AvisarDocumentosEmpresaVencendoJob : IAvisarDocumentosEmpresaVencendoJob
{
    public const string JobId = "avisar-documentos-empresa-vencendo";

    /// <summary>08:25 de Brasília — logo depois do aviso de certificado.</summary>
    public const string Cron = "25 8 * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AvisarDocumentosEmpresaVencendoJob> _logger;

    public AvisarDocumentosEmpresaVencendoJob(IServiceScopeFactory scopeFactory, ILogger<AvisarDocumentosEmpresaVencendoJob> logger)
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
                    .Where(e => e.Ativo
                        && db.Tenants.Any(t => t.Id == e.TenantId && t.Ativo)
                        && db.DocumentosEmpresa.IgnoreQueryFilters().Any(d => d.EmpresaId == e.Id && d.ValidaAte != null))
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

                var resultado = await scope.ServiceProvider.GetRequiredService<IAvisarDocumentosEmpresaVencendoUseCase>()
                    .ExecutarAsync(empresaId, hoje, CancellationToken.None);

                if (resultado.Avisados > 0)
                    _logger.LogInformation("Aviso de {Quantidade} documento(s) da Empresa {EmpresaId} enviado para {Destinatarios}.",
                        resultado.Avisados, empresaId, string.Join(", ", resultado.Destinatarios));
            }
            catch (Exception ex)
            {
                falhas++;
                _logger.LogError(ex, "Falha ao verificar os documentos da Empresa {EmpresaId}.", empresaId);
            }
        }

        if (falhas > 0 && falhas == empresas.Count)
            throw new InvalidOperationException($"Aviso de documentos falhou em todas as {falhas} empresa(s) — ver log.");
    }
}
