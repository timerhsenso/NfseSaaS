using FluentValidation;
using NfseSaaS.Application.UseCases.Catalogos;

namespace NfseSaaS.Application.UseCases.Servicos;

public sealed class CadastrarServicoRequestValidator : AbstractValidator<CadastrarServicoRequest>
{
    public CadastrarServicoRequestValidator(
        IBuscarCodigoTributacaoNacionalUseCase buscarCodigoTributacaoNacional,
        IBuscarCodigoNbsUseCase buscarCodigoNbs)
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(500);

        RuleFor(x => x.CodigoTributacaoNacional)
            .NotEmpty()
            .MaximumLength(20)
            // Existência real no catálogo oficial (CodigoTributacaoNacionalSeeder),
            // não só formato — um código que não existe seria rejeitado
            // pela própria SEFIN na hora de emitir, então barrar aqui é
            // mais cedo e mais claro pro usuário do que só formato.
            .MustAsync(async (codigo, cancellationToken) =>
                !string.IsNullOrWhiteSpace(codigo) && await buscarCodigoTributacaoNacional.ExisteAsync(codigo, cancellationToken))
            .WithMessage("Código de tributação nacional (cTribNac) não encontrado no catálogo oficial.");

        RuleFor(x => x.CodigoNbs)
            .NotEmpty()
            .MaximumLength(20)
            // Mesmo raciocínio do cTribNac acima — CodigoNbsSeeder.
            .MustAsync(async (codigo, cancellationToken) =>
                !string.IsNullOrWhiteSpace(codigo) && await buscarCodigoNbs.ExisteAsync(codigo, cancellationToken))
            .WithMessage("Código NBS não encontrado no catálogo oficial.");

        RuleFor(x => x.ValorPadrao).GreaterThanOrEqualTo(0);
    }
}
