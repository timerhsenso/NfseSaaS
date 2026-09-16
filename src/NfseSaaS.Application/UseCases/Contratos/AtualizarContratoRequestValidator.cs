using FluentValidation;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed class AtualizarContratoRequestValidator : AbstractValidator<AtualizarContratoRequest>
{
    public AtualizarContratoRequestValidator()
    {
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(200);

        // Ver comentário completo em CadastrarContratoRequestValidator —
        // mesmo raciocínio, backstop server-side da regra que o JS já
        // aplica na tela (aplicarRegraTipoCobranca em contratos.js).
        RuleFor(x => x.PeriodicidadeReajusteMeses)
            .GreaterThan(0).LessThanOrEqualTo(60)
            .When(x => x.TipoCobranca == TipoCobrancaContrato.Mensal);
        RuleFor(x => x.PeriodicidadeReajusteMeses)
            .Equal(0).WithMessage("Periodicidade de reajuste não se aplica a cobrança Avulsa.")
            .When(x => x.TipoCobranca == TipoCobrancaContrato.Avulso);

        RuleFor(x => x.IndiceReajuste).IsInEnum().When(x => x.IndiceReajuste.HasValue);
        RuleFor(x => x.IndiceReajuste)
            .NotNull().WithMessage("Índice de reajuste é obrigatório para cobrança Mensal.")
            .When(x => x.TipoCobranca == TipoCobrancaContrato.Mensal);
        RuleFor(x => x.IndiceReajuste)
            .Null().WithMessage("Índice de reajuste não se aplica a cobrança Avulsa.")
            .When(x => x.TipoCobranca == TipoCobrancaContrato.Avulso);

        RuleFor(x => x.Observacao).MaximumLength(2000);

        RuleFor(x => x.DiasAlertaOverride).GreaterThan(0).When(x => x.DiasAlertaOverride.HasValue);
        RuleFor(x => x.DiasAlertaOverride)
            .Null().WithMessage("Alerta de reajuste não se aplica a cobrança Avulsa.")
            .When(x => x.TipoCobranca == TipoCobrancaContrato.Avulso);
    }
}
