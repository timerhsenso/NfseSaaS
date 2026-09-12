using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Só serve a view — dados vêm via fetch do api/clientes já existente.</summary>
[RequerPermissao(TelaCatalogo.Clientes, AcaoPermissao.Consultar)]
public sealed class ClientesController : Controller
{
    public IActionResult Index() => View();
}
