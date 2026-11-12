using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Aparencia;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Painel "Aparência" do topo. Qualquer usuário logado lê e salva as
/// PRÓPRIAS preferências; a paleta da conta só o administrador (conferido
/// no serviço, não só escondido na tela).
/// </summary>
[ApiController]
[Authorize]
[Route("api/aparencia")]
public sealed class AparenciaController : ControllerBase
{
    private readonly IAparenciaUseCase _aparencia;

    public AparenciaController(IAparenciaUseCase aparencia)
    {
        _aparencia = aparencia;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
        => Ok(await _aparencia.ObterAsync(cancellationToken));

    [HttpPut("preferencias")]
    public async Task<IActionResult> SalvarPreferencias([FromBody] PreferenciasAparencia preferencias, CancellationToken cancellationToken)
        => Ok(await _aparencia.SalvarPreferenciasAsync(preferencias, cancellationToken));

    [HttpPut("paleta")]
    public async Task<IActionResult> SalvarPaleta([FromBody] SalvarPaletaRequest request, CancellationToken cancellationToken)
    {
        await _aparencia.SalvarPaletaAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Logo da conta (B9). Qualquer usuário da conta vê; a URL leva a versão
    /// (?v=…), então o navegador guarda a imagem e só busca de novo quando
    /// ela muda. nosniff: o navegador nunca trata a imagem como outra coisa.
    /// </summary>
    [HttpGet("logo")]
    public async Task<IActionResult> Logo([FromQuery(Name = "v")] string? versao, CancellationToken cancellationToken)
    {
        var logo = await _aparencia.ObterLogoAsync(cancellationToken);
        if (logo is null)
            return NotFound();

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = versao == logo.Versao
            ? "private, max-age=31536000, immutable"
            : "private, no-cache";
        return File(logo.Conteudo, logo.TipoConteudo);
    }

    /// <summary>Envia o logo (PNG, JPG ou WebP, até 512 KB). Só administrador, plano Escritório.</summary>
    [HttpPut("logo")]
    [RequestSizeLimit(RegrasLogoConta.TamanhoMaximoBytes + 64 * 1024)]
    public async Task<IActionResult> SalvarLogo(IFormFile arquivo, CancellationToken cancellationToken)
    {
        if (arquivo is null || arquivo.Length == 0)
            return BadRequest(new { erro = "Escolha a imagem da logomarca." });
        if (arquivo.Length > RegrasLogoConta.TamanhoMaximoBytes)
            return BadRequest(new { erro = $"A logomarca pode ter até {RegrasLogoConta.TamanhoMaximoBytes / 1024} KB. Reduza a imagem e envie de novo." });

        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, cancellationToken);
        var versao = await _aparencia.SalvarLogoAsync(memoria.ToArray(), cancellationToken);
        return Ok(new { versao });
    }

    [HttpDelete("logo")]
    public async Task<IActionResult> RemoverLogo(CancellationToken cancellationToken)
    {
        await _aparencia.RemoverLogoAsync(cancellationToken);
        return NoContent();
    }
}
