using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Authorization;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Garante que os IdentityRole padrão do produto (ver Papeis) existem no
/// banco. Roda uma vez no startup (ver Program.cs) — idempotente: só cria
/// o que ainda não existe, seguro de rodar em todo boot da aplicação.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var papel in Papeis.Todos)
        {
            if (!await roleManager.RoleExistsAsync(papel))
                await roleManager.CreateAsync(new IdentityRole<Guid>(papel));
        }
    }
}
