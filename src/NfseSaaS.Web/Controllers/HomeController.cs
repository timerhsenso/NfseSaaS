using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    /// <summary>
    /// "/": quem está logado vê o painel; quem não está vê a página
    /// inicial pública do micoempresa (apresentação, preços e cadastro).
    /// </summary>
    [AllowAnonymous]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
            return View("~/Views/Site/Inicio.cshtml");

        return View();
    }

    /// <summary>
    /// Página de erro amigável pra requisições de tela (MVC/Razor).
    /// ExceptionHandlingMiddleware redireciona pra cá quando uma exceção
    /// não tratada estoura fora de /api — o log completo (stack trace,
    /// tipo, CorrelationId) já foi gravado lá; aqui só exibimos o
    /// CorrelationId pro usuário poder informar ao suporte. [AllowAnonymous]
    /// porque o erro pode acontecer antes da autenticação estar
    /// estabelecida (ex.: sessão expirada no meio de uma navegação).
    /// </summary>
    [AllowAnonymous]
    [PermitirSemAceiteTermos]
    [Route("Home/Error")]
    // Sem verbo HTTP de propósito (o UseExceptionHandler reexecuta com o
    // método original: GET, POST, PUT...). Fica fora do Swagger, que não
    // aceita action sem verbo e quebrava o /swagger/v1/swagger.json.
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Error(string? cid)
    {
        return View(new ErrorViewModel(cid ?? HttpContext.TraceIdentifier));
    }
}

public sealed record ErrorViewModel(string CorrelationId);
