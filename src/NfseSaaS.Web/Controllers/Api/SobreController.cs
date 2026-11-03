using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Web.Filters;
using NfseSaaS.Web.Services;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
/// <summary>Versão/commit do deploy — informação de operação, restrita ao operador da plataforma.</summary>
[RequerOperadorPlataforma]
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
