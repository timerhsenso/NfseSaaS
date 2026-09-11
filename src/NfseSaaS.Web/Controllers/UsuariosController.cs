using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Convite de usuário + grid com TODOS os usuários do Tenant (pendentes,
/// ativos, bloqueados): trocar papel, bloquear/desbloquear acesso,
/// reenviar convite e excluir. Ver api/auth/usuarios* (AuthController).
/// </summary>
[Authorize(Roles = Papeis.Administrador)]
public sealed class UsuariosController : Controller
{
    public IActionResult Index() => View();
}
