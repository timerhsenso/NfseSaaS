using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.UseCases.Cadastro;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Apoio à tela pública de cadastro (Account/Registrar), antes de existir
/// login. Só leitura e com limite por IP ("ConsultaCadastro"), para a página
/// não virar consulta grátis de CNPJ de terceiros.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/cadastro")]
public sealed class CadastroController : ControllerBase
{
    private readonly IConsultarCnpjCadastroUseCase _consultarCnpj;
    private readonly PlataformaOptions _plataforma;

    public CadastroController(IConsultarCnpjCadastroUseCase consultarCnpj, IOptions<PlataformaOptions> plataforma)
    {
        _consultarCnpj = consultarCnpj;
        _plataforma = plataforma.Value;
    }

    /// <summary>"Sua empresa: XYZ LTDA, Salvador/BA" — nome, cidade, situação e se já há conta para o CNPJ.</summary>
    [EnableRateLimiting("ConsultaCadastro")]
    [HttpGet("cnpj/{cnpj}")]
    public async Task<IActionResult> ConsultarCnpj(string cnpj, CancellationToken cancellationToken)
    {
        if (!_plataforma.CadastroAberto)
            return StatusCode(StatusCodes.Status403Forbidden, new { erro = "O cadastro de novas contas está fechado no momento." });
        return Ok(await _consultarCnpj.ExecutarAsync(cnpj, cancellationToken));
    }
}
