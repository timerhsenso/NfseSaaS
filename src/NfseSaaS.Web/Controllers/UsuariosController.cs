using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Convite de usuário + grid com TODOS os usuários do Tenant (pendentes,
/// ativos, bloqueados): trocar grupo de permissão, bloquear/desbloquear
/// acesso, reenviar convite e excluir. Ver api/auth/usuarios*
/// (AuthController) e o módulo de segurança IAEC (Grupo/GrupoTela).
/// </summary>
[RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Consultar)]
public sealed class UsuariosController : Controller
{
    public IActionResult Index() => View();
}
