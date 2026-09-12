using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Módulo de segurança IAEC — matriz de permissão editável por Grupo. Ver GruposController (Api).</summary>
[RequerPermissao(TelaCatalogo.Grupos, AcaoPermissao.Consultar)]
public sealed class GruposController : Controller
{
    public IActionResult Index() => View();
}
