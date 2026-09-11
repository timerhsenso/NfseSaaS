using FluentValidation;

namespace NfseSaaS.Application.UseCases.NotaMensal;

public sealed class EmitirNotaMensalLoteRequestValidator : AbstractValidator<EmitirNotaMensalLoteRequest>
{
    public EmitirNotaMensalLoteRequestValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.Competencia).NotEqual(default(DateOnly));
        RuleFor(x => x.Itens).NotEmpty().WithMessage("Selecione ao menos um contrato.");

        RuleForEach(x => x.Itens).ChildRules(item =>
        {
            item.RuleFor(i => i.ContratoId).NotEmpty();
            item.RuleFor(i => i.ValorTotalAjustado).GreaterThan(0).When(i => i.ValorTotalAjustado.HasValue);
        });
    }
}
