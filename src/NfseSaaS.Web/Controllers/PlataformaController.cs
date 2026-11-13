using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Painel do operador da plataforma: todas as contas, uso e suspensão. Dados via api/plataforma.</summary>
[RequerOperadorPlataforma]
public sealed class PlataformaController : Controller
{
    public IActionResult Index() => View();

    /// <summary>Catálogo de planos, preços (com histórico) e vagas de fundador. Dados via api/plataforma/planos.</summary>
    public IActionResult Planos() => View();

    /// <summary>DOC4: catálogo geral de documentos, pacotes por ramo e links por UF/município. Dados via api/plataforma/documentos.</summary>
    public IActionResult Documentos() => View();
}
