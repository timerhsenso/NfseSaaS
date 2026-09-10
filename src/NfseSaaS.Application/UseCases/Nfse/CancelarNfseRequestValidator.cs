using FluentValidation;

namespace NfseSaaS.Application.UseCases.Nfse;

public sealed class CancelarNfseRequestValidator : AbstractValidator<CancelarNfseRequest>
{
    public CancelarNfseRequestValidator()
    {
        // Não validamos contra uma lista fechada de códigos — ver comentário em CancelarNfseRequest.
        RuleFor(x => x.CodigoMotivo).GreaterThan(0);

        // TSMotivo (leiaute da SEFIN Nacional): 15 a 255 caracteres.
        // Confirmado contra uma rejeição real (E1235, "actual length is
        // less than the MinLength value") em 09/09/2026 — o máximo de
        // 500 que estava aqui também estava errado, o real é 255.
        //
        // Valida no texto JÁ APARADO (Trim) — os builders (DpsBuilder/
        // EventoCancelamentoBuilder) removem espaço do início/fim antes
        // de escrever no XML (outra rejeição real confirmada: E1235
        // "Pattern constraint failed" no TSMotivo, causada por espaço
        // sobrando no fim do texto). Validar o texto cru aceitaria como
        // válido, por exemplo, 14 caracteres + 1 espaço — que passaria
        // aqui mas viraria 14 caracteres reais depois do Trim.
        RuleFor(x => x.Motivo)
            .NotEmpty()
            .Must(m => m.Trim().Length >= 15).WithMessage("O motivo precisa ter pelo menos 15 caracteres, sem contar espaços no início/fim (exigência da SEFIN Nacional).")
            .Must(m => m.Trim().Length <= 255).WithMessage("O motivo pode ter no máximo 255 caracteres, sem contar espaços no início/fim.");
    }
}
