using FluentValidation;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed class CadastrarContratoRequestValidator : AbstractValidator<CadastrarContratoRequest>
{
    public CadastrarContratoRequestValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.ServicoId).NotEmpty();
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ValorAtual).GreaterThan(0);
        RuleFor(x => x.PeriodicidadeReajusteMeses).GreaterThan(0).LessThanOrEqualTo(60);
        RuleFor(x => x.IndiceReajuste).MaximumLength(30);
        RuleFor(x => x.DiasAlertaOverride).GreaterThan(0).When(x => x.DiasAlertaOverride.HasValue);
    }
}
