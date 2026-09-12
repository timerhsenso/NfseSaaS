using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só serve a view — dados vêm via fetch do api/logs. Restrito a quem
/// tem Consultar em Logs (hoje só Administrador).
/// </summary>
[RequerPermissao(TelaCatalogo.Logs, AcaoPermissao.Consultar)]
public sealed class LogsController : Controller
{
    public IActionResult Index() => View();
}
