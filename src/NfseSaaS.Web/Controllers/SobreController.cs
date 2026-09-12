using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

[RequerPermissao(TelaCatalogo.Sobre, AcaoPermissao.Consultar)]
public sealed class SobreController : Controller
{
    public IActionResult Index() => View();
}
