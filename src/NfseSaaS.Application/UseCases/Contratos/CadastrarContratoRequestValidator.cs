using FluentValidation;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed class CadastrarContratoRequestValidator : AbstractValidator<CadastrarContratoRequest>
{
    public CadastrarContratoRequestValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.Descricao).MaximumLength(200);
        RuleFor(x => x.Numero).MaximumLength(NumeroContratoRegras.TamanhoMaximo).WithName("Número do contrato");
        RuleFor(x => x.Status).IsInEnum();

        // Reajuste (periodicidade/índice/alerta) só se aplica a cobrança
        // Mensal — Avulso é emitido uma vez, não tem recorrência pra
        // reajustar. Espelha o que o JS já trava na tela (ver
        // aplicarRegraTipoCobranca em contratos.js) — aqui é o backstop
        // server-side, pra não confiar só na UI.
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

        // IM1: data do último reajuste já aplicado (contrato vindo de outro sistema).
        RuleFor(x => x.DataUltimoReajuste)
            .Null().WithMessage("Último reajuste não se aplica a cobrança Avulsa.")
            .When(x => x.TipoCobranca == TipoCobrancaContrato.Avulso);
        RuleFor(x => x.DataUltimoReajuste)
            .Must((x, data) => data > x.DataInicioContrato)
            .WithMessage("O último reajuste tem que ser depois do início do contrato.")
            .When(x => x.DataUltimoReajuste.HasValue);
        RuleFor(x => x.DataUltimoReajuste)
            .Must(data => data <= NfseSaaS.Application.Common.DataBrasilia.Hoje())
            .WithMessage("O último reajuste não pode ser uma data futura.")
            .When(x => x.DataUltimoReajuste.HasValue);

        RuleFor(x => x.DiasAlertaOverride).GreaterThan(0).When(x => x.DiasAlertaOverride.HasValue);
        RuleFor(x => x.DiasAlertaOverride)
            .Null().WithMessage("Alerta de reajuste não se aplica a cobrança Avulsa.")
            .When(x => x.TipoCobranca == TipoCobrancaContrato.Avulso);

        RuleFor(x => x.Servicos).NotEmpty().WithMessage("Informe ao menos um serviço.");
        RuleForEach(x => x.Servicos).ChildRules(servico =>
        {
            servico.RuleFor(s => s.ServicoId).NotEmpty();
            servico.RuleFor(s => s.Quantidade).GreaterThan(0);
            servico.RuleFor(s => s.ValorUnitario).GreaterThan(0);
        });

        // Mesmo Serviço duas vezes no mesmo Contrato não faz sentido —
        // ajustar Quantidade/ValorUnitario da linha existente resolve o
        // mesmo caso de uso. Validado aqui pra não depender só do JS
        // (a UI já impede escolher o mesmo Serviço em duas linhas, isso
        // é o backstop).
        RuleFor(x => x.Servicos)
            .Must(servicos => servicos.Select(s => s.ServicoId).Distinct().Count() == servicos.Count)
            .WithMessage("O mesmo Serviço não pode aparecer em mais de uma linha do Contrato.")
            .When(x => x.Servicos is { Count: > 0 });
    }
}
