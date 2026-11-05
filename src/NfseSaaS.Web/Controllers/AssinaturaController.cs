using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Tela "Assinatura" — dados via api/assinatura.</summary>
[RequerPermissao(TelaCatalogo.Assinatura, AcaoPermissao.Consultar)]
public sealed class AssinaturaController : Controller
{
    public IActionResult Index() => View();
}
