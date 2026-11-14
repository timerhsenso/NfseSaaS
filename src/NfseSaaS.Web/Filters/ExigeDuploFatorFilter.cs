using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Seguranca;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// SEG1 — verificação em duas etapas obrigatória (ver
/// <see cref="RegrasDuploFator.Avaliar"/>). Acabou o prazo (ou é o operador
/// da plataforma) e ainda está desligada: o sistema só abre a tela de ligar.
/// - tela (MVC): redireciona para /Seguranca/DuasEtapas?returnUrl=...;
/// - API: 403 com { erro, codigo: "duplo_fator_pendente", url } — o apiFetch
///   (wwwroot/js/site.js) leva para a tela.
/// Filtro GLOBAL (Program.cs), depois do ExigeAceiteTermosFilter: nenhuma
/// rota nova escapa por esquecimento. Anônimo passa (quem barra é o
/// [Authorize]). Exceções: <see cref="PermitirSemDuploFatorAttribute"/> e
/// <see cref="PermitirSemAceiteTermosAttribute"/>. O /hangfire é middleware:
/// HangfireDashboardAuthorizationFilter confere o mesmo pelo claim.
/// </summary>
public sealed class ExigeDuploFatorFilter : IAsyncActionFilter
{
    private readonly IExigenciaDuploFatorService _exigencia;
    private readonly ICurrentUser _currentUser;

    public ExigeDuploFatorFilter(IExigenciaDuploFatorService exigencia, ICurrentUser currentUser)
    {
        _exigencia = exigencia;
        _currentUser = currentUser;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var metadados = context.ActionDescriptor.EndpointMetadata;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true
            || metadados.OfType<PermitirSemDuploFatorAttribute>().Any()
            || metadados.OfType<PermitirSemAceiteTermosAttribute>().Any()
            || _currentUser.UserId is not { } usuarioId)
        {
            await next();
            return;
        }

        var exigencia = await _exigencia.AvaliarAsync(usuarioId, context.HttpContext.RequestAborted);
        if (!exigencia.Bloqueia)
        {
            await next();
            return;
        }

        var ehApi = metadados.OfType<IApiBehaviorMetadata>().Any() || context.HttpContext.Request.Path.StartsWithSegments("/api");
        context.Result = ehApi
            ? new ObjectResult(new
            {
                erro = RegrasDuploFator.MensagemBloqueio,
                codigo = RegrasDuploFator.CodigoPendente,
                url = RegrasDuploFator.CaminhoTelaObrigatoria
            })
            { StatusCode = StatusCodes.Status403Forbidden }
            : new RedirectResult(MontarUrlTela(context.HttpContext.Request));
    }

    /// <summary>Volta para a mesma tela depois de ligar. Só GET tem para onde voltar.</summary>
    public static string MontarUrlTela(HttpRequest request)
    {
        var destino = HttpMethods.IsGet(request.Method)
            ? request.PathBase + request.Path + request.QueryString
            : "/";
        return destino == "/"
            ? RegrasDuploFator.CaminhoTelaObrigatoria
            : $"{RegrasDuploFator.CaminhoTelaObrigatoria}?returnUrl={Uri.EscapeDataString(destino)}";
    }
}
