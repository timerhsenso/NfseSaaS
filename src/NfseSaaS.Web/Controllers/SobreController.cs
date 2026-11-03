using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Versão/commit do deploy — informação de operação, restrita ao operador da plataforma.</summary>
[RequerOperadorPlataforma]
public sealed class SobreController : Controller
{
    public IActionResult Index() => View();
}
