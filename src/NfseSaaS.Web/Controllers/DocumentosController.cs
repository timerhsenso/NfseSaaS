using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>DOC1: tela "Documentos da empresa" — só a view; dados via api/documentos-empresa.</summary>
[RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Consultar)]
public sealed class DocumentosController : Controller
{
    public IActionResult Index() => View();
}
