using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Consultas;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Utilitário de preenchimento automático — não grava nada, só consulta
/// uma fonte pública (BrasilAPI, que agrega dados da Receita Federal) e
/// devolve o que encontrar. Aberto a qualquer usuário autenticado.
/// </summary>
[ApiController]
[Authorize]
[Route("api/consultas")]
public sealed class ConsultasController : ControllerBase
{
    private readonly IConsultarCnpjUseCase _consultarCnpj;

    public ConsultasController(IConsultarCnpjUseCase consultarCnpj)
    {
        _consultarCnpj = consultarCnpj;
    }

    [HttpGet("cnpj/{cnpj}")]
    public async Task<IActionResult> ConsultarCnpj(string cnpj, CancellationToken cancellationToken)
    {
        var resultado = await _consultarCnpj.ExecutarAsync(cnpj, cancellationToken);
        return Ok(resultado);
    }
}
