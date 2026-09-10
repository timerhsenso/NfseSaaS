using FluentValidation;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public sealed class RegistrarReajusteRequestValidator : AbstractValidator<RegistrarReajusteRequest>
{
    public RegistrarReajusteRequestValidator()
    {
        RuleFor(x => x.DataReajuste).NotEqual(default(DateOnly)).WithMessage("DataReajuste é obrigatória.");
        RuleFor(x => x.ValorNovo).GreaterThan(0);
        RuleFor(x => x.IndiceUsado).MaximumLength(30);
        RuleFor(x => x.Observacao).MaximumLength(500);
    }
}
