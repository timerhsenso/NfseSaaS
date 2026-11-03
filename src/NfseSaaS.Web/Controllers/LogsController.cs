using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só serve a view — dados vêm via fetch do api/logs.
/// Restrito ao operador da plataforma: o log do Serilog é GLOBAL (todos
/// os tenants no mesmo arquivo) — liberar pro Administrador de um tenant
/// expunha dados dos outros. Ver RequerOperadorPlataformaAttribute.
/// </summary>
[RequerOperadorPlataforma]
public sealed class LogsController : Controller
{
    public IActionResult Index() => View();
}
