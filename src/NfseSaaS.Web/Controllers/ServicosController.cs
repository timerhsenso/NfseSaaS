using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>Só serve a view — dados vêm via fetch do api/servicos já existente.</summary>
[Authorize]
public sealed class ServicosController : Controller
{
    public IActionResult Index() => View();
}
