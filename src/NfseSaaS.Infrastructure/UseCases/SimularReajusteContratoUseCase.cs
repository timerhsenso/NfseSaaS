using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Simulação do reajuste pelo índice (ver <see cref="ISimularReajusteContratoUseCase"/>).
/// Reaproveita o acumulado do reajuste automático e a regra única de
/// <see cref="ReajusteContrato"/> — nada de conta própria aqui.
/// </summary>
public sealed class SimularReajusteContratoUseCase : ISimularReajusteContratoUseCase
{
    private readonly AppDbContext _db;
    private readonly ICalcularIndiceAcumuladoUseCase _calcularAcumulado;

    public SimularReajusteContratoUseCase(AppDbContext db, ICalcularIndiceAcumuladoUseCase calcularAcumulado)
    {
        _db = db;
        _calcularAcumulado = calcularAcumulado;
    }

    public async Task<SimulacaoReajusteResponse> ExecutarAsync(Guid contratoId, SimularReajusteRequest request, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        var linhas = await _db.ContratoServicos.AsNoTracking()
            .Where(cs => cs.ContratoId == contratoId)
            .ToListAsync(cancellationToken);
        var parteVariavel = ReajusteContrato.ParteVariavel(linhas);
        var idsVariaveis = parteVariavel == 0
            ? new List<Guid>()
            : linhas.Where(l => l.ValorVariavel).Select(l => l.ServicoId).Distinct().ToList();
        var foraDoReajuste = idsVariaveis.Count == 0
            ? Array.Empty<string>()
            : (await _db.Servicos.AsNoTracking()
                .Where(s => idsVariaveis.Contains(s.Id))
                .Select(s => s.Descricao)
                .ToListAsync(cancellationToken))
                .Order(StringComparer.CurrentCulture)
                .ToArray();

        var hoje = DataBrasilia.Hoje();
        DateOnly? aniversario = contrato.PeriodicidadeReajusteMeses > 0
            ? SituacaoContratoCalculator.CalcularDataProximoReajuste(contrato)
            : null;
        var dataSugerida = DataSugeridaReajuste.Calcular(aniversario, hoje);
        var data = request.DataReajuste ?? dataSugerida;
        var indice = request.Indice ?? contrato.IndiceReajuste;

        SimulacaoReajusteResponse Indisponivel(string mensagem, IndiceAcumuladoResponse? acumulado = null) => new(
            indice, data, dataSugerida, aniversario, false, mensagem,
            acumulado?.CompetenciaInicio, acumulado?.CompetenciaFim, null, null, false, contrato.AplicarIndiceNegativo,
            contrato.ValorAtual, parteVariavel, null, foraDoReajuste,
            acumulado?.Meses ?? Array.Empty<IndiceAcumuladoMesResponse>());

        if (indice is null)
            return Indisponivel("Escolha o índice para calcular o reajuste.");
        if (indice == IndiceReajusteContrato.Outro)
            return Indisponivel("\"Outro\" não tem tabela de índice: informe o valor novo ou o percentual.");
        if (contrato.ValorAtual <= 0)
            return Indisponivel("O contrato está sem valor: informe o valor novo.");

        var acumulado = await _calcularAcumulado.ExecutarAsync(indice.Value, data, cancellationToken);
        if (!acumulado.Disponivel || acumulado.PercentualAcumulado is not { } percentualIndice)
            return Indisponivel(acumulado.Mensagem ?? "Não foi possível calcular o acumulado do índice.", acumulado);

        var reduz = request.AplicarNegativo ?? contrato.AplicarIndiceNegativo;
        var percentual = ReajusteContrato.PercentualEfetivo(percentualIndice, reduz);
        var valorNovo = ReajusteContrato.CalcularValorNovo(contrato.ValorAtual, parteVariavel, percentual);

        return new SimulacaoReajusteResponse(
            indice, data, dataSugerida, aniversario, true, null,
            acumulado.CompetenciaInicio, acumulado.CompetenciaFim, percentualIndice, percentual,
            percentual != percentualIndice, contrato.AplicarIndiceNegativo,
            contrato.ValorAtual, parteVariavel, valorNovo, foraDoReajuste, acumulado.Meses);
    }
}
