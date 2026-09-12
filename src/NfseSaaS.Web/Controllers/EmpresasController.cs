using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só serve a view — toda leitura/escrita de Empresa acontece via fetch
/// pro EmpresasController de api/empresas (Controllers/Api), a mesma API
/// usada por Swagger/clients externos. Nenhuma lógica de negócio
/// duplicada aqui.
/// </summary>
[RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Consultar)]
public sealed class EmpresasController : Controller
{
    public IActionResult Index() => View();
}
