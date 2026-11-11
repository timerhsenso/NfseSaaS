using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Dá número aos contratos que nasceram antes do campo (11/11/2026):
/// por empresa, na ordem da data de início (e do cadastro), "ano/NNNN" com
/// o ano do início do contrato, continuando depois dos números que a
/// empresa já tenha nesse padrão. Idempotente: só olha contratos sem
/// número — depois da primeira subida não acha mais nada.
/// Roda fora de requisição: sem filtro de conta, gravando com ExecuteUpdate.
/// </summary>
public static class NumeroContratoBackfill
{
    public static async Task PreencherAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NumeroContratoBackfill));

        var semNumero = await db.Contratos.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Numero == null)
            .Select(c => new { c.Id, c.EmpresaId, c.DataInicioContrato, c.CreatedAt })
            .ToListAsync();
        if (semNumero.Count == 0)
            return;

        var empresaIds = semNumero.Select(c => c.EmpresaId).Distinct().ToList();
        var jaNumerados = (await db.Contratos.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Numero != null && empresaIds.Contains(c.EmpresaId))
                .Select(c => new { c.EmpresaId, c.Numero })
                .ToListAsync())
            .GroupBy(c => c.EmpresaId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Numero).ToList());

        var preenchidos = 0;
        foreach (var empresa in semNumero.GroupBy(c => c.EmpresaId))
        {
            var usados = jaNumerados.GetValueOrDefault(empresa.Key) ?? new List<string?>();
            foreach (var contrato in empresa.OrderBy(c => c.DataInicioContrato).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id))
            {
                var numero = NumeroContratoRegras.Proximo(contrato.DataInicioContrato.Year, usados);
                usados.Add(numero);

                await db.Contratos.IgnoreQueryFilters()
                    .Where(c => c.Id == contrato.Id && c.Numero == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Numero, numero));
                preenchidos++;
            }
        }

        logger.LogInformation("Número de contrato preenchido em {Quantidade} contrato(s) antigos.", preenchidos);
    }
}
