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

    [HttpGet("/Account/EsqueciSenha")]
    public IActionResult EsqueciSenha() => View();

    [HttpGet("/Account/RedefinirSenha")]
    public IActionResult RedefinirSenha(string? email = null, string? token = null)
    {
        ViewData["Email"] = email;
        ViewData["Token"] = token;
        return View();
    }

    /// <summary>
    /// Destino padrão do Identity (AccessDeniedPath) quando um usuário
    /// autenticado tenta uma tela MVC sem permissão — rota que faltava
    /// no projeto (só existia pra /api, tratado à parte em
    /// OnRedirectToAccessDenied) e nunca tinha sido exercitada até o
    /// módulo de segurança IAEC ([RequerPermissao]) tornar esse caminho
    /// alcançável de verdade.
    /// </summary>
    [HttpGet("/Account/AccessDenied")]
    public IActionResult AccessDenied(string? returnUrl = null) => View();
}
