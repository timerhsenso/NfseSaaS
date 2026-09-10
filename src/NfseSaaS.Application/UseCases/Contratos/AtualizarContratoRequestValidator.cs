using FluentValidation;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed class AtualizarContratoRequestValidator : AbstractValidator<AtualizarContratoRequest>
{
    public AtualizarContratoRequestValidator()
    {
        RuleFor(x => x.ServicoId).NotEmpty();
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PeriodicidadeReajusteMeses).GreaterThan(0).LessThanOrEqualTo(60);
        RuleFor(x => x.IndiceReajuste).MaximumLength(30);
        RuleFor(x => x.DiasAlertaOverride).GreaterThan(0).When(x => x.DiasAlertaOverride.HasValue);
    }
}
