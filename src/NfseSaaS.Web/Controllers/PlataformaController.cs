using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Painel do operador da plataforma: todas as contas, uso e suspensão. Dados via api/plataforma.</summary>
[RequerOperadorPlataforma]
public sealed class PlataformaController : Controller
{
    public IActionResult Index() => View();
}
