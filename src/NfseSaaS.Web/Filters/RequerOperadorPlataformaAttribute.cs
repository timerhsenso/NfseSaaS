using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NfseSaaS.Web.Authorization;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Restringe a action/controller ao operador da plataforma (dono do SaaS).
/// Usado nas telas que enxergam dados de TODOS os tenants (Logs, Sobre) —
/// nelas a permissão IAEC do tenant (RequerPermissaoAttribute) não serve,
/// porque o Administrador de qualquer tenant pode se dar qualquer Tela.
/// Ver PlataformaClaims e PlataformaOptions.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequerOperadorPlataformaAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!user.EhOperadorPlataforma())
            context.Result = new ForbidResult();
    }
}
