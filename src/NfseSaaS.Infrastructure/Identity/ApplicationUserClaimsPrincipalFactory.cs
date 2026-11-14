using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Infrastructure.MultiTenancy;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Estende os Claims padrão do ASP.NET Core Identity com "tenant_id" e,
/// desde o módulo de segurança IAEC, com um Claim de permissão por Tela
/// que o Grupo do usuário libera (ver Grupo/GrupoTela) — é o que
/// ClaimsPrincipalExtensions e RequerPermissaoAttribute leem. Roda
/// automaticamente sempre que o Identity gera o cookie de autenticação
/// (login, registro) — mecanismo de extensão padrão do próprio Identity,
/// não uma customização por fora dele.
///
/// Também roda na revalidação periódica do cookie (SecurityStampValidator,
/// a cada 1 minuto — ver DependencyInjection): mudança de Grupo ou de
/// permissão chega a quem já está logado em até 1 minuto, sem relogar.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    /// <summary>Prefixo do tipo do Claim — o Codigo da Tela vem concatenado logo em seguida (ex.: "perm:Clientes").</summary>
    public const string PermissaoClaimTypePrefix = "perm:";

    private readonly AppDbContext _db;
    private readonly PlataformaOptions _plataforma;

    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> options,
        AppDbContext db,
        IOptions<PlataformaOptions> plataforma)
        : base(userManager, roleManager, options)
    {
        _db = db;
        _plataforma = plataforma.Value;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(CurrentTenant.TenantIdClaimType, user.TenantId.ToString()));

        // Operador da plataforma (dono do SaaS): fora da matriz do tenant,
        // decidido só pela configuração — ver PlataformaOptions.
        if (_plataforma.UsuariosOperadoresIds.Contains(user.Id))
            identity.AddClaim(new Claim(PlataformaClaims.Operador, "true"));

        if (user.TwoFactorEnabled)
            identity.AddClaim(new Claim(PlataformaClaims.DuploFatorLigado, "true"));

        if (user.GrupoId is { } grupoId)
        {
            var permissoes = await _db.GrupoTelas
                .AsNoTracking()
                .IgnoreQueryFilters() // login roda fora de uma requisição com ICurrentTenant resolvido — filtra manualmente por GrupoId, que já é suficiente pra unicidade
                .Where(gt => gt.GrupoId == grupoId)
                .Join(_db.Telas.AsNoTracking(), gt => gt.TelaId, t => t.Id, (gt, t) => new { t.Codigo, gt.Incluir, gt.Alterar, gt.Excluir, gt.Consultar })
                .ToListAsync();

            foreach (var permissao in permissoes)
            {
                var letras = string.Concat(
                    permissao.Incluir ? "I" : "",
                    permissao.Alterar ? "A" : "",
                    permissao.Excluir ? "E" : "",
                    permissao.Consultar ? "C" : "");

                if (letras.Length == 0)
                    continue; // sem nenhuma letra == sem acesso nenhum à tela — não precisa nem gravar o Claim

                identity.AddClaim(new Claim(PermissaoClaimTypePrefix + permissao.Codigo, letras));
            }
        }

        return identity;
    }
}
