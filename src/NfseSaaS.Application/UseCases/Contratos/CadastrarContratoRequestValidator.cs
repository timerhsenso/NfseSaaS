using FluentValidation;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed class CadastrarContratoRequestValidator : AbstractValidator<CadastrarContratoRequest>
{
    public CadastrarContratoRequestValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PeriodicidadeReajusteMeses).GreaterThan(0).LessThanOrEqualTo(60);
        RuleFor(x => x.IndiceReajuste).MaximumLength(30);
        RuleFor(x => x.DiasAlertaOverride).GreaterThan(0).When(x => x.DiasAlertaOverride.HasValue);

        RuleFor(x => x.Servicos).NotEmpty().WithMessage("Informe ao menos um serviço.");
        RuleForEach(x => x.Servicos).ChildRules(servico =>
        {
            servico.RuleFor(s => s.ServicoId).NotEmpty();
            servico.RuleFor(s => s.Quantidade).GreaterThan(0);
            servico.RuleFor(s => s.ValorUnitario).GreaterThan(0);
        });

        // Mesmo Serviço duas vezes no mesmo Contrato não faz sentido —
        // ajustar Quantidade/ValorUnitario da linha existente resolve o
        // mesmo caso de uso. Validado aqui pra não depender só do JS
        // (a UI já impede escolher o mesmo Serviço em duas linhas, isso
        // é o backstop).
        RuleFor(x => x.Servicos)
            .Must(servicos => servicos.Select(s => s.ServicoId).Distinct().Count() == servicos.Count)
            .WithMessage("O mesmo Serviço não pode aparecer em mais de uma linha do Contrato.")
            .When(x => x.Servicos is { Count: > 0 });
    }
}
