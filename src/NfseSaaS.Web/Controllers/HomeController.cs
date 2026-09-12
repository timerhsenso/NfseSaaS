using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

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
    [Route("Home/Error")]
    public IActionResult Error(string? cid)
    {
        return View(new ErrorViewModel(cid ?? HttpContext.TraceIdentifier));
    }
}

public sealed record ErrorViewModel(string CorrelationId);
