using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Onboarding;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// "Primeiros passos" do painel. Qualquer usuário logado da conta vê o
/// progresso (só lê contagens da própria conta); cada passo leva à tela
/// onde ele é feito, que continua protegida pela permissão dela.
/// </summary>
[ApiController]
[Authorize]
[Route("api/onboarding")]
public sealed class OnboardingController : ControllerBase
{
    private readonly IObterProgressoOnboardingUseCase _obterProgresso;

    public OnboardingController(IObterProgressoOnboardingUseCase obterProgresso)
    {
        _obterProgresso = obterProgresso;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
        => Ok(await _obterProgresso.ExecutarAsync(cancellationToken));
}
