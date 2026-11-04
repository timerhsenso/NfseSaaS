using System.Globalization;
using Microsoft.EntityFrameworkCore;
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
        if (valorAnterior != 0 && valorNovo != valorAnterior)
        {
            var fator = valorNovo / valorAnterior;
            foreach (var linha in linhas)
                linha.ValorUnitario = Math.Round(linha.ValorUnitario * fator, 2, MidpointRounding.AwayFromZero);
        }

        contrato.ValorAtual = valorNovo;
        contrato.DataUltimoReajuste = evento.DataVigencia;

        evento.Tipo = TipoMovimentacaoValor.Reajuste;
        evento.Status = StatusMovimentacaoValor.Aplicado;
        evento.ValorAnterior = valorAnterior;
        evento.ValorNovo = valorNovo;
        evento.PercentualAplicado = percentualAplicado
            ?? (valorAnterior == 0 ? null : Math.Round((valorNovo - valorAnterior) / valorAnterior * 100, 4));
        evento.ComposicaoJson = await ComposicaoContrato.SerializarAsync(
            db, linhas.Select(l => (l.ServicoId, l.Quantidade, l.ValorUnitario)), cancellationToken);
    }
}
