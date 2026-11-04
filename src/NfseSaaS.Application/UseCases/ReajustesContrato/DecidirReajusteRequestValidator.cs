using FluentValidation;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public sealed class DecidirReajusteRequestValidator : AbstractValidator<DecidirReajusteRequest>
{
    public DecidirReajusteRequestValidator()
    {
        RuleFor(x => x.Acao).IsInEnum();
        RuleFor(x => x.Motivo).MaximumLength(500);

        // Negociar ou recusar é sair do que o contrato diz — fica registrado o porquê.
        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("Informe o motivo (ex.: \"cliente pediu redução, acordado 3%\").")
            .When(x => x.Acao is AcaoDecisaoReajuste.AplicarNegociado or AcaoDecisaoReajuste.Recusar);

        RuleFor(x => x)
            .Must(x => x.PercentualNegociado.HasValue ^ x.ValorNovoNegociado.HasValue)
            .WithName("PercentualNegociado")
            .WithMessage("Informe o percentual negociado OU o valor novo (um dos dois).")
            .When(x => x.Acao == AcaoDecisaoReajuste.AplicarNegociado);

        RuleFor(x => x.PercentualNegociado!.Value)
            .InclusiveBetween(-50m, 100m).WithName("PercentualNegociado")
            .When(x => x.Acao == AcaoDecisaoReajuste.AplicarNegociado && x.PercentualNegociado.HasValue);

        RuleFor(x => x.ValorNovoNegociado!.Value)
            .GreaterThan(0).WithName("ValorNovoNegociado")
            .When(x => x.Acao == AcaoDecisaoReajuste.AplicarNegociado && x.ValorNovoNegociado.HasValue);
    }
}
