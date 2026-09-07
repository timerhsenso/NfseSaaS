using FluentValidation;

namespace NfseSaaS.Application.UseCases.Nfse;

public sealed class CancelarNfseRequestValidator : AbstractValidator<CancelarNfseRequest>
{
    public CancelarNfseRequestValidator()
    {
        // Não validamos contra uma lista fechada de códigos — ver comentário em CancelarNfseRequest.
        RuleFor(x => x.CodigoMotivo).GreaterThan(0);
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(500);
    }
}
