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
