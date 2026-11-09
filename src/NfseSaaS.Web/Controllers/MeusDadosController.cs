using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>Tela "Meus dados" (exportação dos dados da conta) — dados via api/dados-conta.</summary>
[Authorize]
public sealed class MeusDadosController : Controller
{
    public IActionResult Index() => View();
}
