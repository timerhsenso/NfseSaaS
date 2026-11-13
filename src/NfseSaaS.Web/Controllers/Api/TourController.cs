using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Tour;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>TR1: quais tours guiados o usuário logado já viu (tour.js).</summary>
[ApiController]
[Authorize]
[Route("api/tour")]
public sealed class TourController : ControllerBase
{
    private readonly ITourUseCase _tour;

    public TourController(ITourUseCase tour) => _tour = tour;

    [HttpGet]
    public async Task<IActionResult> Vistos(CancellationToken cancellationToken) =>
        Ok(await _tour.VistosAsync(cancellationToken));

    [HttpPost("{codigo}/visto")]
    public async Task<IActionResult> MarcarVisto(string codigo, CancellationToken cancellationToken)
    {
        await _tour.MarcarVistoAsync(codigo, cancellationToken);
        return NoContent();
    }

    /// <summary>"Rever os tours": todos voltam a abrir sozinhos.</summary>
    [HttpDelete]
    public async Task<IActionResult> ReverTodos(CancellationToken cancellationToken)
    {
        await _tour.ReverTodosAsync(cancellationToken);
        return NoContent();
    }
}
