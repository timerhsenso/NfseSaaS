using FluentValidation;

namespace NfseSaaS.Application.UseCases.Exportacoes;

public sealed class RegistrarExportacaoRequestValidator : AbstractValidator<RegistrarExportacaoRequest>
{
    public RegistrarExportacaoRequestValidator()
    {
        RuleFor(x => x.Tela).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Formato).NotEmpty().MaximumLength(30);
    }
}
