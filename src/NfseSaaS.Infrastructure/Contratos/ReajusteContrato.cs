using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.HistoricoValoresContrato;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Contratos;

/// <summary>
/// Regra única de "aplicar um reajuste" no Contrato — usada pelo reajuste
/// manual (RegistrarReajusteUseCase), pela aprovação de um reajuste
/// pendente (DecidirReajusteUseCase) e pelo reajuste automático do
/// aniversário (GerarReajustesAniversarioUseCase). Não chama SaveChanges:
/// quem chama decide a unidade de trabalho.
/// </summary>
public static class ReajusteContrato
{
    /// <summary>valorAtual × (1 + p/100), 2 casas, arredondamento comercial.</summary>
    public static decimal CalcularValor(decimal valorAtual, decimal percentual) =>
        Math.Round(valorAtual * (1m + percentual / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Quanto do valor do Contrato NÃO entra no reajuste: as linhas de valor
    /// variável (ex.: HelpDesk, que muda a cada nota — o valor do cadastro é
    /// só uma referência). Contrato só com linhas variáveis reajusta tudo
    /// (comportamento de antes): senão o reajuste não teria efeito nenhum.
    /// </summary>
    public static decimal ParteVariavel(IReadOnlyCollection<ContratoServico> linhas)
    {
        if (!linhas.Any(l => !l.ValorVariavel))
            return 0m;

        return linhas.Where(l => l.ValorVariavel)
            .Sum(l => Math.Round(l.Quantidade * l.ValorUnitario, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Valor novo do Contrato com o percentual aplicado só na parte fixa:
    /// variável + (atual − variável) × (1 + p/100). Mesma conta da simulação,
    /// do reajuste automático e da aprovação.
    /// </summary>
    public static decimal CalcularValorNovo(decimal valorAtual, decimal parteVariavel, decimal percentual) =>
        parteVariavel + CalcularValor(valorAtual - parteVariavel, percentual);

    public static async Task<decimal> CalcularValorNovoAsync(
        AppDbContext db, Contrato contrato, decimal percentual, CancellationToken cancellationToken)
    {
        var linhas = await db.ContratoServicos.AsNoTracking()
            .Where(cs => cs.ContratoId == contrato.Id)
            .ToListAsync(cancellationToken);
        return CalcularValorNovo(contrato.ValorAtual, ParteVariavel(linhas), percentual);
    }

    /// <summary>Índice negativo só reduz o valor se o Contrato permitir; senão o reajuste é 0% (mantém o valor).</summary>
    public static decimal PercentualEfetivo(decimal percentualCalculado, bool aplicarIndiceNegativo) =>
        percentualCalculado < 0 && !aplicarIndiceNegativo ? 0m : percentualCalculado;

    public static bool ReajusteAutomaticoEfetivo(Contrato contrato, Empresa empresa) =>
        contrato.ReajusteAutomatico ?? empresa.AplicarReajusteAutomatico;

    /// <summary>
    /// Só um reajuste por vez: com um aguardando aprovação, registrar outro
    /// (manual ou pelas linhas) criaria dois valores "novos" concorrentes.
    /// </summary>
    public static async Task GarantirSemPendenteAsync(AppDbContext db, Guid contratoId, CancellationToken cancellationToken)
    {
        var pendente = await db.MovimentacoesValorContrato.AsNoTracking()
            .Where(m => m.ContratoId == contratoId && m.Status == StatusMovimentacaoValor.Pendente)
            .Select(m => (DateOnly?)m.DataVigencia)
            .FirstOrDefaultAsync(cancellationToken);

        if (pendente is { } vigencia)
            throw new RegraNegocioException(
                $"Há um reajuste aguardando aprovação (vigência {vigencia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}). Aplique ou recuse esse reajuste no Histórico de valores antes de registrar outro.");
    }

    /// <summary>
    /// Aplica <paramref name="valorNovo"/> ao Contrato e completa o evento:
    /// escala cada linha pelo mesmo fator (soma das linhas ≈ ValorAtual,
    /// centavos de diferença aceitos — mesma limitação de sempre), grava a
    /// composição resultante, marca o evento como Aplicado e move a data
    /// do último reajuste para a vigência do evento.
    /// </summary>
    public static async Task AplicarAsync(
        AppDbContext db,
        Contrato contrato,
        MovimentacaoValorContrato evento,
        decimal valorNovo,
        decimal? percentualAplicado,
        CancellationToken cancellationToken)
    {
        var valorAnterior = contrato.ValorAtual;

        var linhas = await db.ContratoServicos.Where(cs => cs.ContratoId == contrato.Id).ToListAsync(cancellationToken);

        // Reajuste é da parte fixa: linha variável fica com o valor de
        // referência que tem (ver ParteVariavel).
        var parteVariavel = ParteVariavel(linhas);
        var baseReajustavel = valorAnterior - parteVariavel;
        if (baseReajustavel != 0 && valorNovo != valorAnterior)
        {
            var fator = (valorNovo - parteVariavel) / baseReajustavel;
            foreach (var linha in linhas.Where(l => parteVariavel == 0 || !l.ValorVariavel))
                linha.ValorUnitario = Math.Round(linha.ValorUnitario * fator, 2, MidpointRounding.AwayFromZero);
        }

        contrato.ValorAtual = valorNovo;
        contrato.DataUltimoReajuste = evento.DataVigencia;

        evento.Tipo = TipoMovimentacaoValor.Reajuste;
        evento.Status = StatusMovimentacaoValor.Aplicado;
        evento.ValorAnterior = valorAnterior;
        evento.ValorNovo = valorNovo;
        evento.PercentualAplicado = PercentualVariacao.CabeNaColuna(percentualAplicado)
            ?? PercentualVariacao.Calcular(valorAnterior, valorNovo, baseReajustavel);
        evento.ComposicaoJson = await ComposicaoContrato.SerializarAsync(
            db, linhas.Select(l => (l.ServicoId, l.Quantidade, l.ValorUnitario)), cancellationToken);
    }
}
