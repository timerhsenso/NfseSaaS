using FluentValidation;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed class AtualizarContratoRequestValidator : AbstractValidator<AtualizarContratoRequest>
{
    public AtualizarContratoRequestValidator()
    {
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PeriodicidadeReajusteMeses).GreaterThan(0).LessThanOrEqualTo(60);
        RuleFor(x => x.IndiceReajuste).IsInEnum().When(x => x.IndiceReajuste.HasValue);
        RuleFor(x => x.Observacao).MaximumLength(2000);
        RuleFor(x => x.DiasAlertaOverride).GreaterThan(0).When(x => x.DiasAlertaOverride.HasValue);
    }
}
