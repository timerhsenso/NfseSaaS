using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Log de todo e-mail que o sistema tenta enviar, com status e reenvio. Ver EmailLogsController (Api).</summary>
[RequerPermissao(TelaCatalogo.Emails, AcaoPermissao.Consultar)]
public sealed class EmailsController : Controller
{
    public IActionResult Index() => View();
}
