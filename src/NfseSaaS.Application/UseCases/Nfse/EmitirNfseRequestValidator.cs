using FluentValidation;

namespace NfseSaaS.Application.UseCases.Nfse;

public sealed class EmitirNfseRequestValidator : AbstractValidator<EmitirNfseRequest>
{
    public EmitirNfseRequestValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.ServicoId).NotEmpty();
        RuleFor(x => x.ContratoId).NotEqual(Guid.Empty).When(x => x.ContratoId.HasValue);

        RuleFor(x => x.ValorServico).GreaterThan(0);

        RuleFor(x => x.DescricaoServico).NotEmpty().MaximumLength(2000);

        RuleFor(x => x.DataCompetencia)
            .NotEqual(default(DateOnly)).WithMessage("DataCompetencia é obrigatória.")
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            .WithMessage("DataCompetencia não pode estar no futuro.");

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(100)
            .When(x => x.IdempotencyKey is not null);
    }
}
