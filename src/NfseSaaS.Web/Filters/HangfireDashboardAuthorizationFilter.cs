using Hangfire.Dashboard;
using NfseSaaS.Web.Authorization;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Autorização do dashboard do Hangfire (/hangfire): SÓ o operador da
/// plataforma (ver PlataformaClaims/PlataformaOptions).
///
/// O dashboard é GLOBAL — mostra e deixa disparar ("Trigger now") os jobs
/// de TODOS os tenants, inclusive a automação de Nota Mensal que emite
/// NFS-e com o certificado de cada Empresa. Antes ele reaproveitava a
/// permissão de Logs do tenant, que todo grupo Administrador recebe no
/// auto-cadastro: qualquer pessoa que se registrasse conseguia disparar
/// emissão na Empresa de outro cliente.
///
/// O dashboard é middleware puro, não passa pelo pipeline de MVC, por
/// isso a checagem é esta interface do Hangfire e não
/// RequerOperadorPlataformaAttribute — a regra é a mesma
/// (ClaimsPrincipalExtensions.EhOperadorPlataforma). Desde o SEG1 o
/// operador também precisa da verificação em duas etapas ligada (o filtro
/// global ExigeDuploFatorFilter não alcança o middleware).
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.EhOperadorPlataforma()
        && context.GetHttpContext().User.HasClaim(NfseSaaS.Application.Authorization.PlataformaClaims.DuploFatorLigado, "true");
}
