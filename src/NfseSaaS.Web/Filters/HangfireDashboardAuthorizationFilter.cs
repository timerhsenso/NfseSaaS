using System.Security.Claims;
using Hangfire.Dashboard;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Autorização do dashboard do Hangfire (/hangfire). Mesma checagem de
/// ValidacaoAntiforgeryFilter/RequerPermissaoAttribute (lê o Claim de
/// permissão já carregado no login, sem chamada ao banco), mas
/// implementando a interface própria do Hangfire — o dashboard é
/// middleware puro, não passa pelo pipeline de MVC, então
/// [RequerPermissao] não se aplica aqui.
///
/// Reaproveita a permissão de TelaCatalogo.Logs (Consultar) em vez de
/// criar uma Tela nova só pra isto: dashboard de jobs é a mesma
/// categoria de "visibilidade de infraestrutura" que a tela de Logs já
/// é — hoje só o grupo Administrador tem Consultar em Logs (ver
/// GrupoProvisionamentoService), então já nasce restrito do jeito certo
/// sem precisar mexer em seed nem em matriz de permissão.
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;

        if (user.Identity?.IsAuthenticated != true)
            return false;

        var valor = user.FindFirstValue(ApplicationUserClaimsPrincipalFactory.PermissaoClaimTypePrefix + TelaCatalogo.Logs);
        return valor is not null && valor.Contains('C');
    }
}
