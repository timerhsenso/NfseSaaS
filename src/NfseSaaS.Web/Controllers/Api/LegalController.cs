using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

public sealed record AceitarTermosRequest(bool Aceito);

/// <summary>
/// Aceite das versões vigentes dos Termos de uso e da Política de
/// privacidade pelo usuário logado (tela /Legal/Aceitar). Cadastro e
/// convite gravam o aceite direto em AuthController.
/// </summary>
[ApiController]
[Route("api/legal")]
[Authorize]
[PermitirSemAceiteTermos]
public sealed class LegalController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAceiteTermosService _aceiteTermos;
    private readonly ICurrentUser _currentUser;

    public LegalController(AppDbContext db, IAceiteTermosService aceiteTermos, ICurrentUser currentUser)
    {
        _db = db;
        _aceiteTermos = aceiteTermos;
        _currentUser = currentUser;
    }

    /// <summary>Versões vigentes e se o usuário logado já aceitou.</summary>
    [HttpGet("situacao")]
    public async Task<IActionResult> Situacao(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return Unauthorized();

        return Ok(new
        {
            versaoTermosDeUso = DocumentosLegais.VersaoTermosDeUso,
            versaoPoliticaPrivacidade = DocumentosLegais.VersaoPoliticaPrivacidade,
            vigentesDesde = DocumentosLegais.VigentesDesde,
            aceito = await _aceiteTermos.AceitouVersaoVigenteAsync(usuarioId, cancellationToken),
            aceitoVersaoAtual = await _aceiteTermos.AceitouVersaoAtualAsync(usuarioId, cancellationToken)
        });
    }

    /// <summary>Grava o aceite (versões, data, IP, navegador). Repetir é inofensivo: se já aceitou, não grava de novo.</summary>
    [HttpPost("aceitar")]
    public async Task<IActionResult> Aceitar([FromBody] AceitarTermosRequest request, CancellationToken cancellationToken)
    {
        if (!request.Aceito)
            return BadRequest(new { erro = "Marque a caixa confirmando que leu e aceita os Termos de uso e a Política de privacidade." });

        if (_currentUser.UserId is not { } usuarioId)
            return Unauthorized();

        // Aceite antecipado: quem ainda está na versão anterior pode aceitar a atual antes de ela valer.
        if (await _aceiteTermos.AceitouVersaoAtualAsync(usuarioId, cancellationToken))
            return NoContent();

        _aceiteTermos.Registrar(usuarioId, OrigemAceiteTermos.Atualizacao);
        await _db.SaveChangesAsync(cancellationToken);

        _aceiteTermos.MarcarComoAceito(usuarioId);

        return NoContent();
    }
}
