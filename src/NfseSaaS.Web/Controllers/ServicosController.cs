using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Só serve a view — dados vêm via fetch do api/servicos já existente.</summary>
[RequerPermissao(TelaCatalogo.Servicos, AcaoPermissao.Consultar)]
public sealed class ServicosController : Controller
{
    public IActionResult Index() => View();
}
