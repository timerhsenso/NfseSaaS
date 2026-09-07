using FluentValidation;

namespace NfseSaaS.Application.UseCases.Servicos;

public sealed class CadastrarServicoRequestValidator : AbstractValidator<CadastrarServicoRequest>
{
    public CadastrarServicoRequestValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CodigoTributacaoNacional).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CodigoNbs).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ValorPadrao).GreaterThanOrEqualTo(0);
    }
}
