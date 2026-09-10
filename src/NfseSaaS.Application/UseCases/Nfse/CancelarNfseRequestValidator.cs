using FluentValidation;

namespace NfseSaaS.Application.UseCases.Nfse;

public sealed class CancelarNfseRequestValidator : AbstractValidator<CancelarNfseRequest>
{
    public CancelarNfseRequestValidator()
    {
        // Não validamos contra uma lista fechada de códigos — ver comentário em CancelarNfseRequest.
        RuleFor(x => x.CodigoMotivo).GreaterThan(0);

        // TSMotivo (leiaute da SEFIN Nacional): 15 a 255 caracteres.
        // Confirmado contra uma rejeição real (E1235, "actual length is
        // less than the MinLength value") em 09/09/2026 — o máximo de
        // 500 que estava aqui também estava errado, o real é 255.
        RuleFor(x => x.Motivo)
            .NotEmpty()
            .MinimumLength(15).WithMessage("O motivo precisa ter pelo menos 15 caracteres (exigência da SEFIN Nacional).")
            .MaximumLength(255);
    }
}
