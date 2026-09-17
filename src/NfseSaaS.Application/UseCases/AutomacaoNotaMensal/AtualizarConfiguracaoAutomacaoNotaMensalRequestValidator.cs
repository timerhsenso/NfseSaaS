using FluentValidation;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.AutomacaoNotaMensal;

public sealed class AtualizarConfiguracaoAutomacaoNotaMensalRequestValidator : AbstractValidator<AtualizarConfiguracaoAutomacaoNotaMensalRequest>
{
    public AtualizarConfiguracaoAutomacaoNotaMensalRequestValidator()
    {
        RuleFor(x => x.Frequencia).IsInEnum();
        RuleFor(x => x.Modo).IsInEnum();

        // Mesmo raciocínio já usado em Contrato (Avulso/Mensal): cada
        // frequência exige exatamente o campo que faz sentido pra ela,
        // e os outros dois têm que vir vazios — validado no servidor,
        // não só escondido/preenchido pela tela.
        RuleFor(x => x.DiaSemana)
            .NotNull().WithMessage("Dia da semana é obrigatório para frequência Semanal.")
            .When(x => x.Frequencia == FrequenciaAutomacaoNotaMensal.Semanal);
        RuleFor(x => x.DiaSemana)
            .Null().WithMessage("Dia da semana só se aplica à frequência Semanal.")
            .When(x => x.Frequencia != FrequenciaAutomacaoNotaMensal.Semanal);

        RuleFor(x => x.DiaDoMes)
            .NotNull().WithMessage("Dia do mês é obrigatório para frequência Mensal.")
            .When(x => x.Frequencia == FrequenciaAutomacaoNotaMensal.Mensal);
        RuleFor(x => x.DiaDoMes)
            .InclusiveBetween(1, 28).WithMessage("Dia do mês precisa estar entre 1 e 28 (evita cair num mês sem esse dia, como fevereiro).")
            .When(x => x.Frequencia == FrequenciaAutomacaoNotaMensal.Mensal && x.DiaDoMes.HasValue);
        RuleFor(x => x.DiaDoMes)
            .Null().WithMessage("Dia do mês só se aplica à frequência Mensal.")
            .When(x => x.Frequencia != FrequenciaAutomacaoNotaMensal.Mensal);
    }
}
