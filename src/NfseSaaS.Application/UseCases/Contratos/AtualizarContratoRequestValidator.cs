using FluentValidation;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Application.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed class AtualizarContratoRequestValidator : AbstractValidator<AtualizarContratoRequest>
{
    public AtualizarContratoRequestValidator()
    {
        RuleFor(x => x.Descricao).MaximumLength(200);
        RuleFor(x => x.Numero).MaximumLength(NumeroContratoRegras.TamanhoMaximo).WithName("Número do contrato");
        RuleFor(x => x.Numero).Must(n => n is null || !string.IsNullOrWhiteSpace(n))
            .WithMessage("Informe o número do contrato.");
        RuleFor(x => x.Status).IsInEnum();

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

        // Mesmas regras de linha do CadastrarContratoRequestValidator, só
        // quando as linhas vierem (nulo = não altera a composição).
        When(x => x.Servicos is not null, () =>
        {
            RuleFor(x => x.Servicos).NotEmpty().WithMessage("O Contrato precisa de ao menos um serviço.");
            RuleForEach(x => x.Servicos).ChildRules(servico =>
            {
                servico.RuleFor(s => s.ServicoId).NotEmpty();
                servico.RuleFor(s => s.Quantidade).GreaterThan(0);
                servico.RuleFor(s => s.ValorUnitario).GreaterThan(0);
            });
            RuleFor(x => x.Servicos)
                .Must(servicos => servicos!.Select(s => s.ServicoId).Distinct().Count() == servicos!.Count)
                .WithMessage("O mesmo Serviço não pode aparecer em mais de uma linha do Contrato.")
                .When(x => x.Servicos is { Count: > 0 });
        });

        // Valor inicial só nasce no cadastro — edição é reajuste ou alteração.
        RuleFor(x => x.TipoAlteracaoServicos)
            .Must(t => t is TipoMovimentacaoValor.Reajuste or TipoMovimentacaoValor.Alteracao)
            .WithMessage("Informe se a mudança nos serviços é um reajuste ou uma alteração de escopo.")
            .When(x => x.TipoAlteracaoServicos.HasValue);

        RuleFor(x => x.MotivoAlteracaoServicos).MaximumLength(Domain.Entities.MovimentacaoValorContrato.TamanhoMaximoMotivo);

        // Por enquanto o valor novo passa a valer na hora — vigência futura
        // (agendada) chega com o reajuste automático.
        RuleFor(x => x.VigenciaAlteracaoServicos)
            .Must(d => d <= DataBrasilia.Hoje())
            .WithMessage("A vigência da alteração não pode ser uma data futura.")
            .When(x => x.VigenciaAlteracaoServicos.HasValue);
    }
}
