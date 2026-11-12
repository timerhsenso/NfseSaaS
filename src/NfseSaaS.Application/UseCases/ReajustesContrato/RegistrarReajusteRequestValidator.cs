using FluentValidation;
using NfseSaaS.Application.Common;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public sealed class RegistrarReajusteRequestValidator : AbstractValidator<RegistrarReajusteRequest>
{
    public RegistrarReajusteRequestValidator()
    {
        RuleFor(x => x.DataReajuste).NotEqual(default(DateOnly)).WithMessage("DataReajuste é obrigatória.");
        RuleFor(x => x.ValorNovo).GreaterThan(0);
        RuleFor(x => x.IndiceUsado).IsInEnum().When(x => x.IndiceUsado.HasValue);
        RuleFor(x => x.Observacao).MaximumLength(500);
        RuleFor(x => x.MotivoSubstituicao)
            .Must(m => (m?.Trim().Length ?? 0) >= RegrasDesfazerReajuste.TamanhoMinimoMotivo)
            .WithMessage($"Explique por que o reajuste anterior está sendo substituído (mínimo {RegrasDesfazerReajuste.TamanhoMinimoMotivo} caracteres).")
            .When(x => x.SubstituirReajusteId.HasValue);
        RuleFor(x => x.MotivoSubstituicao).MaximumLength(500);

        // O valor novo passa a valer na hora (ValorAtual do Contrato muda já)
        // — vigência futura (agendada) chega com o reajuste automático.
        RuleFor(x => x.DataReajuste)
            .Must(d => d <= DataBrasilia.Hoje())
            .WithMessage("A data do reajuste não pode ser futura.");
    }
}
