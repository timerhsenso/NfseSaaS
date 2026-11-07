using FluentValidation;
using NfseSaaS.Application.UseCases.Catalogos;

namespace NfseSaaS.Application.UseCases.Servicos;

public sealed class AtualizarServicoRequestValidator : AbstractValidator<AtualizarServicoRequest>
{
    public AtualizarServicoRequestValidator(
        IBuscarCodigoTributacaoNacionalUseCase buscarCodigoTributacaoNacional,
        IBuscarCodigoNbsUseCase buscarCodigoNbs)
    {
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(500);

        RuleFor(x => x.CodigoTributacaoNacional)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (codigo, cancellationToken) =>
                !string.IsNullOrWhiteSpace(codigo) && await buscarCodigoTributacaoNacional.ExisteAsync(codigo, cancellationToken))
            .WithMessage("Código de tributação nacional (cTribNac) não encontrado no catálogo oficial.");

        // Código que exige dados de obra ou de evento, que ainda não
        // enviamos na DPS: a SEFIN rejeitaria (CodigosTributacaoNaoSuportados).
        RuleFor(x => x.CodigoTributacaoNacional)
            .Must(codigo => CodigosTributacaoNaoSuportados.Motivo(codigo) is null)
            .WithMessage(x => CodigosTributacaoNaoSuportados.Motivo(x.CodigoTributacaoNacional));


        RuleFor(x => x.CodigoNbs)
            .NotEmpty()
            .MaximumLength(20)
            .MustAsync(async (codigo, cancellationToken) =>
                !string.IsNullOrWhiteSpace(codigo) && await buscarCodigoNbs.ExisteAsync(codigo, cancellationToken))
            .WithMessage("Código NBS não encontrado no catálogo oficial.");

        RuleFor(x => x.ValorPadrao).GreaterThanOrEqualTo(0);
    }
}
