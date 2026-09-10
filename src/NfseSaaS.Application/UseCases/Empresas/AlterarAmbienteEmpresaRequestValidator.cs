using FluentValidation;

namespace NfseSaaS.Application.UseCases.Empresas;

public sealed class AlterarAmbienteEmpresaRequestValidator : AbstractValidator<AlterarAmbienteEmpresaRequest>
{
    public AlterarAmbienteEmpresaRequestValidator()
    {
        RuleFor(x => x.TipoAmbiente).IsInEnum();
    }
}
