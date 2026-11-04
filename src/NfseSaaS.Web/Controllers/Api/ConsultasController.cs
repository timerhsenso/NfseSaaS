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
    private readonly IConsultarCepUseCase _consultarCep;
    private readonly IConsultarMunicipioUseCase _consultarMunicipio;

    public ConsultasController(
        IConsultarCnpjUseCase consultarCnpj,
        IConsultarCepUseCase consultarCep,
        IConsultarMunicipioUseCase consultarMunicipio)
    {
        _consultarCnpj = consultarCnpj;
        _consultarCep = consultarCep;
        _consultarMunicipio = consultarMunicipio;
    }

    /// <summary>Endereço + código IBGE do município a partir do CEP (ViaCEP).</summary>
    [HttpGet("cep/{cep}")]
    public async Task<IActionResult> ConsultarCep(string cep, CancellationToken cancellationToken)
        => Ok(await _consultarCep.ExecutarAsync(cep, cancellationToken));

    /// <summary>Nome e UF do município pelo código IBGE (API de localidades do IBGE).</summary>
    [HttpGet("municipio/{codigo}")]
    public async Task<IActionResult> ConsultarMunicipio(string codigo, CancellationToken cancellationToken)
        => Ok(await _consultarMunicipio.ExecutarAsync(codigo, cancellationToken));

    [HttpGet("cnpj/{cnpj}")]
    public async Task<IActionResult> ConsultarCnpj(string cnpj, CancellationToken cancellationToken)
    {
        var resultado = await _consultarCnpj.ExecutarAsync(cnpj, cancellationToken);
        return Ok(resultado);
    }
}
