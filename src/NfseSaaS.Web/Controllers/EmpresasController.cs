using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só serve a view — toda leitura/escrita de Empresa acontece via fetch
/// pro EmpresasController de api/empresas (Controllers/Api), a mesma API
/// usada por Swagger/clients externos. Nenhuma lógica de negócio
/// duplicada aqui.
/// </summary>
[Authorize]
public sealed class EmpresasController : Controller
{
    public IActionResult Index() => View();
}
