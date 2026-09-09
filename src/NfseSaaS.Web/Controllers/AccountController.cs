using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só serve as views de Login/Registrar/AceitarConvite — a autenticação
/// de fato (criação de sessão, criação de Tenant, validação de convite)
/// continua inteiramente em AuthController (api/auth/*), chamada por
/// estas views via fetch. Nenhuma lógica de negócio duplicada aqui.
///
/// "/Account/Login" é o LoginPath padrão do cookie do ASP.NET Core
/// Identity — por isso o nome/rota não são arbitrários: é pra onde o
/// próprio middleware de autenticação redireciona automaticamente
/// quando uma página MVC protegida é acessada sem sessão.
/// </summary>
[AllowAnonymous]
public sealed class AccountController : Controller
{
    [HttpGet("/Account/Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpGet("/Account/Registrar")]
    public IActionResult Registrar() => View();

    [HttpGet("/Account/AceitarConvite")]
    public IActionResult AceitarConvite(string? email = null, string? token = null)
    {
        ViewData["Email"] = email;
        ViewData["Token"] = token;
        return View();
    }
}
