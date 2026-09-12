using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;
using NfseSaaS.Web.Services;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Só leitura, por design — mesma lógica do AuditLogsController: não
/// existe (e não deve existir) escrita aqui. Restrito a quem tem
/// Consultar em Logs — hoje só o grupo Administrador (ver
/// GrupoProvisionamentoService), porque isto pode conter stack trace de
/// infraestrutura, não é dado de negócio do Tenant.
/// </summary>
[ApiController]
[RequerPermissao(TelaCatalogo.Logs, AcaoPermissao.Consultar)]
[Route("api/logs")]
public sealed class LogsController : ControllerBase
{
    private readonly ILogFileReader _logFileReader;

    public LogsController(ILogFileReader logFileReader)
    {
        _logFileReader = logFileReader;
    }

    [HttpGet("arquivos")]
    public IActionResult ListarArquivos() => Ok(_logFileReader.ListarArquivos());

    [HttpGet("conteudo")]
    public IActionResult ObterConteudo(
        [FromQuery] string arquivo,
        [FromQuery] int linhas = 500,
        [FromQuery] string? nivel = null,
        [FromQuery] string? busca = null)
    {
        try
        {
            return Ok(_logFileReader.LerUltimasLinhas(arquivo, linhas, nivel, busca));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { erro = ex.Message });
        }
        catch (IOException)
        {
            // Arquivo do dia atual, em uso pelo próprio Serilog no
            // instante exato da leitura — já tentamos internamente com
            // retry (ver LogFileReader), isto só acontece se o lock
            // persistiu além disso. Não é erro de aplicação, por isso
            // não vira 500/log de exceção — é só "tente de novo".
            return StatusCode(423, new { erro = "O arquivo está sendo gravado agora pelo sistema. Tente atualizar em alguns segundos." });
        }
    }
}
