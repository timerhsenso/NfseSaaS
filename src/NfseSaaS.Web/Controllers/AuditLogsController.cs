using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só serve a view — dados vêm via fetch do api/auditlogs já existente.
/// Restrito a Administrador, mesma regra da API (é dado de compliance
/// sobre TODAS as operações do Tenant).
/// </summary>
[Authorize(Roles = Papeis.Administrador)]
public sealed class AuditLogsController : Controller
{
    public IActionResult Index() => View();
}
