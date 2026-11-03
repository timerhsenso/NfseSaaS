using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Garante que o catálogo de Telas (ver TelaCatalogo) existe no banco —
/// idempotente, mesmo padrão do IdentitySeeder pros papéis do Identity.
/// Roda uma vez no startup, ANTES de qualquer provisionamento de Grupo
/// (que depende das Telas já existirem).
/// </summary>
public static class TelaSeeder
{
    public static async Task SeedTelasAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Telas que viraram exclusivas do operador da plataforma (Logs,
        // Sobre): apaga as permissões de TODOS os tenants ligadas a elas e
        // depois a própria Tela. ExecuteDelete + IgnoreQueryFilters de
        // propósito — é limpeza de sistema, cruza tenants, e não passa
        // pelo SaveChanges (que exige um tenant atual). Idempotente: na
        // segunda subida não acha mais nada pra apagar.
        var descontinuadas = TelaCatalogo.Descontinuadas;
        var idsDescontinuadas = await db.Telas
            .Where(t => descontinuadas.Contains(t.Codigo))
            .Select(t => t.Id)
            .ToListAsync();

        if (idsDescontinuadas.Count > 0)
        {
            await db.GrupoTelas.IgnoreQueryFilters()
                .Where(gt => idsDescontinuadas.Contains(gt.TelaId))
                .ExecuteDeleteAsync();

            await db.Telas
                .Where(t => idsDescontinuadas.Contains(t.Id))
                .ExecuteDeleteAsync();
        }

        var codigosExistentes = await db.Telas.Select(t => t.Codigo).ToListAsync();

        foreach (var (codigo, nome, ordem) in TelaCatalogo.Todas)
        {
            if (codigosExistentes.Contains(codigo))
                continue;

            db.Telas.Add(new Tela { Codigo = codigo, Nome = nome, Ordem = ordem });
        }

        await db.SaveChangesAsync();
    }
}
