using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Tela "Modelos de e-mail": modelos reutilizáveis do e-mail da nota (um
/// padrão por conta; Empresa e Cliente escolhem o seu). Dados via
/// api/modelos-email-nota; quem não é administrador vê só para leitura.
/// </summary>
[Authorize]
public sealed class ModelosEmailController : Controller
{
    public IActionResult Index() => View();
}
