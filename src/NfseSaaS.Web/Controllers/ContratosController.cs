using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Só serve a view — dados vêm via fetch do api/contratos já existente.</summary>
[RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Consultar)]
public sealed class ContratosController : Controller
{
    public IActionResult Index() => View();
}
