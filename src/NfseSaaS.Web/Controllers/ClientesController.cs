using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>Só serve a view — dados vêm via fetch do api/clientes já existente.</summary>
[Authorize]
public sealed class ClientesController : Controller
{
    public IActionResult Index() => View();
}
