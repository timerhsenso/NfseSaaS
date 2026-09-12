using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;
using NfseSaaS.Web.Services;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[RequerPermissao(TelaCatalogo.Sobre, AcaoPermissao.Consultar)]
[Route("api/sobre")]
public sealed class SobreController : ControllerBase
{
    private readonly IVersaoInfo _versaoInfo;

    public SobreController(IVersaoInfo versaoInfo)
    {
        _versaoInfo = versaoInfo;
    }

    [HttpGet]
    public IActionResult Obter()
    {
        var versao = _versaoInfo.ObterAtual();

        return Ok(new
        {
            versao.Versao,
            versao.Commit,
            versao.DeployEm,
            changelog = _versaoInfo.ObterChangelogMarkdown()
        });
    }
}
