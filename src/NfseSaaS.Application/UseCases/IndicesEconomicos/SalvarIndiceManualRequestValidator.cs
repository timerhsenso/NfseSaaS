using FluentValidation;
using NfseSaaS.Application.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.IndicesEconomicos;

public sealed class SalvarIndiceManualRequestValidator : AbstractValidator<SalvarIndiceManualRequest>
{
    public SalvarIndiceManualRequestValidator()
    {
        RuleFor(x => x.Indice)
            .IsInEnum()
            .NotEqual(IndiceReajusteContrato.Outro).WithMessage("O índice \"Outro\" não tem tabela de variações.");

        RuleFor(x => x.Competencia).NotEqual(default(DateOnly)).WithMessage("Competência é obrigatória.");

        // Mês corrente ainda não fechou — nenhum índice mensal é publicado
        // antes do fim do mês de referência.
        RuleFor(x => x.Competencia)
            .Must(c => new DateOnly(c.Year, c.Month, 1) < new DateOnly(DataBrasilia.Hoje().Year, DataBrasilia.Hoje().Month, 1))
            .WithMessage("A competência precisa ser de um mês já encerrado.");

        // numeric(9,4) e bom senso: nenhum índice de preços brasileiro variou
        // ±50% num mês desde o Plano Real. Barra erro de digitação (56 em vez de 0,56).
        RuleFor(x => x.Variacao)
            .InclusiveBetween(-50m, 50m).WithMessage("Variação mensal fora do intervalo plausível (-50% a 50%).")
            .Must(v => decimal.Round(v, 4) == v).WithMessage("Use no máximo 4 casas decimais.");
    }
}
