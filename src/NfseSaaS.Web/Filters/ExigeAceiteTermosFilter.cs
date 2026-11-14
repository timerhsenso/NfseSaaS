using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Legal;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Usuário logado que ainda não aceitou a versão VIGENTE dos Termos de uso
/// e da Política de privacidade (NfseSaaS.Domain.Legal.DocumentosLegais)
/// não usa o sistema até aceitar:
/// - tela (MVC): redireciona para /Legal/Aceitar?returnUrl=... e, depois do
///   aceite, volta para onde ia;
/// - API: 403 com { erro, codigo: "termos_pendentes", url } — o apiFetch
///   (wwwroot/js/site.js) reconhece o código e leva para a tela de aceite.
///
/// Registrado como filtro GLOBAL (Program.cs), igual ao
/// ValidacaoAntiforgeryFilter, então nenhuma tela nova escapa por
/// esquecimento. Anônimo passa direto (quem barra é o [Authorize] de cada
/// rota). Exceções explícitas: <see cref="PermitirSemAceiteTermosAttribute"/>.
/// O hangfire (/hangfire) é middleware, não passa por aqui — só o operador
/// da plataforma entra nele.
/// </summary>
public sealed class ExigeAceiteTermosFilter : IAsyncActionFilter
{
    public const string CodigoTermosPendentes = "termos_pendentes";

    public const string CaminhoTelaAceite = "/Legal/Aceitar";

    private readonly IAceiteTermosService _aceiteTermos;
    private readonly IAceiteInstrumentosService _instrumentos;
    private readonly ICurrentUser _currentUser;

    public ExigeAceiteTermosFilter(IAceiteTermosService aceiteTermos, IAceiteInstrumentosService instrumentos, ICurrentUser currentUser)
    {
        _aceiteTermos = aceiteTermos;
        _instrumentos = instrumentos;
        _currentUser = currentUser;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true
            || context.ActionDescriptor.EndpointMetadata.OfType<PermitirSemAceiteTermosAttribute>().Any()
            || _currentUser.UserId is not { } usuarioId)
        {
            await next();
            return;
        }

        // JUR1: depois que o DPA passa a valer, o administrador também precisa tê-lo aceito (uma vez por conta).
        if (await _aceiteTermos.AceitouVersaoVigenteAsync(usuarioId, context.HttpContext.RequestAborted)
            && !await _instrumentos.DpaObrigatorioPendenteAsync(usuarioId, context.HttpContext.RequestAborted))
        {
            await next();
            return;
        }

        context.Result = EhApi(context)
            ? new ObjectResult(new
            {
                erro = "Antes de continuar, leia e aceite os Termos de uso e a Política de privacidade.",
                codigo = CodigoTermosPendentes,
                url = CaminhoTelaAceite
            })
            { StatusCode = StatusCodes.Status403Forbidden }
            : new RedirectResult(MontarUrlTelaAceite(context.HttpContext.Request));
    }

    private static bool EhApi(ActionExecutingContext context) =>
        context.ActionDescriptor.EndpointMetadata.OfType<IApiBehaviorMetadata>().Any()
        || context.HttpContext.Request.Path.StartsWithSegments("/api");

    /// <summary>Volta para a mesma tela depois do aceite. Só GET tem pra onde voltar; o resto vai pra raiz.</summary>
    public static string MontarUrlTelaAceite(HttpRequest request)
    {
        var destino = HttpMethods.IsGet(request.Method)
            ? request.PathBase + request.Path + request.QueryString
            : "/";

        return destino == "/"
            ? CaminhoTelaAceite
            : $"{CaminhoTelaAceite}?returnUrl={Uri.EscapeDataString(destino)}";
    }
}
