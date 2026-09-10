using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>Só serve a view — dados vêm via fetch do api/contratos já existente.</summary>
[Authorize]
public sealed class ContratosController : Controller
{
    public IActionResult Index() => View();
}
