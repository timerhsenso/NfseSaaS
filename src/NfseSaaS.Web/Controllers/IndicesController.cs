using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Tabela de índices econômicos (IPCA, IGP-M...). Leitura para qualquer
/// usuário autenticado — é referência pública, igual aos catálogos; lançar,
/// excluir e "Importar agora" só o operador da plataforma (a tabela é
/// GLOBAL, vale para todos os tenants). Dados via api/indices-economicos.
/// </summary>
[Authorize]
public sealed class IndicesController : Controller
{
    public IActionResult Index() => View();
}
