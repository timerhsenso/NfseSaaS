using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>Só serve a view — dados vêm via fetch do api/nfse já existente.</summary>
[RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Consultar)]
public sealed class NfseController : Controller
{
    public IActionResult Index() => View();
}
