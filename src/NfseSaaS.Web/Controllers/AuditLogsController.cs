using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Só serve a view — dados vêm via fetch do api/auditlogs já existente.
/// Restrito a quem tem Consultar em Auditoria, mesma regra da API (é
/// dado de compliance sobre TODAS as operações do Tenant — hoje só
/// Administrador/Financeiro/Contador têm isso, ver GrupoProvisionamentoService).
/// </summary>
[RequerPermissao(TelaCatalogo.Auditoria, AcaoPermissao.Consultar)]
public sealed class AuditLogsController : Controller
{
    public IActionResult Index() => View();
}
