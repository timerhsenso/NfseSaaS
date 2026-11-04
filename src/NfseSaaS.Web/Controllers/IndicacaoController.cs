using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>Tela "Indique e ganhe" — dados via api/indicacao.</summary>
[Authorize]
public sealed class IndicacaoController : Controller
{
    public IActionResult Index() => View();
}
