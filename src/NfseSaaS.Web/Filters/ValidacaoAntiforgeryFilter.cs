using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Roda antes de toda action de método não seguro (POST/PUT/PATCH/DELETE)
/// e valida o par de tokens anti-CSRF: o cookie "de validação" HttpOnly
/// (configurado em AddAntiforgery, Program.cs) contra o header
/// X-CSRF-TOKEN, que o JS ecoa a partir do cookie legível
/// "NfseSaaS.Xsrf-Token" (emitido em toda resposta pelo middleware
/// registrado logo após app.UseRouting() em Program.cs — ver
/// obterCsrfHeader() em wwwroot/js/site.js, usado por apiFetch e por
/// todo fetch feito fora dele).
///
/// Métodos seguros (GET/HEAD/OPTIONS/TRACE) nunca mudam estado — e este
/// projeto não tem nenhum <form> HTML tradicional (tudo é fetch pra
/// /api/*, ver AccountController), então só as actions de API realmente
/// passam por aqui na prática.
///
/// Cobre TODAS as actions (MVC e API) automaticamente, sem precisar
/// decorar cada uma com [ValidateAntiForgeryToken] — mesmo raciocínio do
/// ValidacaoAutomaticaFilter (FluentValidation).
/// </summary>
public sealed class ValidacaoAntiforgeryFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> MetodosSeguros = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace
    };

    private readonly IAntiforgery _antiforgery;

    public ValidacaoAntiforgeryFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!MetodosSeguros.Contains(context.HttpContext.Request.Method))
        {
            // Lança AntiforgeryValidationException se o par não bater —
            // tratada pelo ExceptionHandlingMiddleware, que devolve 403.
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }

        await next();
    }
}
