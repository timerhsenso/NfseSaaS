using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só a tela de convite — não existe (ainda) endpoint de LISTAGEM de
/// usuários na API (api/auth só tem Registrar/Login/Convidar/AceitarConvite),
/// então esta tela não lista quem já foi convidado. Ver nota na própria view.
/// </summary>
[Authorize(Roles = Papeis.Administrador)]
public sealed class UsuariosController : Controller
{
    public IActionResult Index() => View();
}
