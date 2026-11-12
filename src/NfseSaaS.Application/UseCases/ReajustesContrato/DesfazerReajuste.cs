using FluentValidation;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

/// <summary>
/// Desfaz o último reajuste do Contrato (ver <see cref="RegrasDesfazerReajuste"/>):
/// valor, serviços e data do último reajuste voltam ao que eram, e o evento
/// fica no Histórico de valores como Desfeito, com o motivo.
/// </summary>
public interface IDesfazerReajusteUseCase
{
    Task ExecutarAsync(Guid contratoId, Guid movimentacaoId, DesfazerReajusteRequest request, CancellationToken cancellationToken);
}

public sealed record DesfazerReajusteRequest(string? Motivo);

public sealed class DesfazerReajusteRequestValidator : AbstractValidator<DesfazerReajusteRequest>
{
    public DesfazerReajusteRequestValidator()
    {
        RuleFor(x => x.Motivo)
            .Must(m => (m?.Trim().Length ?? 0) >= RegrasDesfazerReajuste.TamanhoMinimoMotivo)
            .WithMessage($"Explique por que o reajuste está sendo desfeito (mínimo {RegrasDesfazerReajuste.TamanhoMinimoMotivo} caracteres).");
        RuleFor(x => x.Motivo).MaximumLength(MovimentacaoValorContrato.TamanhoMaximoMotivo);
    }
}
