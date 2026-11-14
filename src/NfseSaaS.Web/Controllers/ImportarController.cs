using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Web.Authorization;

namespace NfseSaaS.Web.Controllers;

/// <summary>IM1: tela "Importar planilha" (dados via api/importacao). Aberta pelos botões de Clientes e Contratos.</summary>
[Authorize]
public sealed class ImportarController : Controller
{
    public IActionResult Index()
    {
        if (!User.PodeIncluir(TelaCatalogo.Clientes) && !User.PodeIncluir(TelaCatalogo.Contratos))
            return Forbid();
        return View();
    }
}
