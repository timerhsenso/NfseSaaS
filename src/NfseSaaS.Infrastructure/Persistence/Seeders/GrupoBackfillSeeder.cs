using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Migração de dado pra Tenant que já existia ANTES do módulo de
/// segurança (Grupo/GrupoTela) — provisiona os 5 grupos padrão e migra
/// cada usuário da Role antiga do Identity pro Grupo correspondente.
/// Idempotente e seguro de rodar em todo boot (só age em Tenant sem
/// nenhum Grupo ainda) — mesmo espírito do IdentitySeeder/TelaSeeder.
///
/// Depois que TODOS os controllers migrarem de [Authorize(Roles=...)]
/// pra [RequerPermissao(...)], este seeder (e a Role em paralelo no
/// ApplicationUser) deixam de ser necessários — ver aviso em
/// AuthController sobre o dual-write transitório.
/// </summary>
public static class GrupoBackfillSeeder
{
    public static async Task BackfillAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var provisionamento = scope.ServiceProvider.GetRequiredService<IGrupoProvisionamentoService>();

        // Precisa ignorar o Global Query Filter (IgnoreQueryFilters) porque
        // este código roda fora de uma requisição autenticada — não há
        // ICurrentTenant.TenantId resolvido, e sem isso o filtro por
        // tenant do Grupo barraria a consulta pra todo mundo.
        var todosOsTenantIds = await db.Tenants.Select(t => t.Id).ToListAsync();
        var tenantIdsComGrupo = await db.Grupos.IgnoreQueryFilters().Select(g => g.TenantId).Distinct().ToListAsync();
        var tenantIdsSemGrupo = todosOsTenantIds.Except(tenantIdsComGrupo).ToList();

        foreach (var tenantId in tenantIdsSemGrupo)
        {
            var gruposPorNome = await provisionamento.ProvisionarGruposPadraoAsync(tenantId, default);

            var usuariosDoTenant = await db.Users
                .Where(u => u.TenantId == tenantId)
                .ToListAsync();

            foreach (var usuario in usuariosDoTenant)
            {
                if (usuario.GrupoId is not null)
                    continue;

                var papeis = await userManager.GetRolesAsync(usuario);
                var papel = papeis.FirstOrDefault();

                if (papel is not null && gruposPorNome.TryGetValue(papel, out var grupoId))
                {
                    usuario.GrupoId = grupoId;
                    await userManager.UpdateAsync(usuario);
                }
                // Usuário sem Role reconhecida (ex.: convite pendente sem
                // papel válido) fica sem GrupoId — trava como "sem
                // permissão nenhuma" até um Administrador corrigir pela
                // tela de Usuários, nunca herda acesso por omissão.
            }
        }
    }
}
