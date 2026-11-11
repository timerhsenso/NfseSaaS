using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Para cada Contrato Ativo da Empresa com índice definido cujo aniversário
/// (data do próximo reajuste) já chegou:
///
/// 1. Calcula o acumulado de 12 meses do índice na data do aniversário
///    (CalculadoraIndiceAcumulado). Índice ainda não publicado → nada é
///    criado; o job tenta de novo no dia seguinte.
/// 2. Acumulado negativo e o Contrato não permite reduzir → 0% (mantém o
///    valor), com o motivo registrado.
/// 3. Reajuste automático (Contrato, ou o padrão da Empresa) → aplica na
///    hora. Senão → evento "aguardando aprovação"; o valor do Contrato só
///    muda quando alguém aprovar (DecidirReajusteUseCase).
///
/// Idempotente: um Contrato com reajuste pendente é pulado, e aplicar ou
/// recusar move a data do último reajuste — o mesmo aniversário nunca gera
/// dois eventos. Aniversário há mais de <see cref="DiasMaximosAtraso"/> dias
/// não é calculado sozinho (Contratos antigos cadastrados com reajustes
/// feitos fora do sistema): esses ficam para o reajuste manual.
/// </summary>
public sealed class GerarReajustesAniversarioUseCase : IGerarReajustesAniversarioUseCase
{
    public const int DiasMaximosAtraso = 60;

    /// <summary>Teto de ciclos aplicados de uma vez num Contrato automático muito atrasado (proteção contra laço).</summary>
    private const int MaximoCiclosPorExecucao = 6;

    private readonly AppDbContext _db;
    private readonly ICalcularIndiceAcumuladoUseCase _calcularAcumulado;
    private readonly IAuditLogWriter _auditLogWriter;

    public GerarReajustesAniversarioUseCase(
        AppDbContext db,
        ICalcularIndiceAcumuladoUseCase calcularAcumulado,
        IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _calcularAcumulado = calcularAcumulado;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<GeracaoReajustesResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == empresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var hoje = DataBrasilia.Hoje();

        var contratos = await _db.Contratos
            .Where(c => c.EmpresaId == empresaId
                && c.Status == StatusContrato.Ativo
                && c.IndiceReajuste != null && c.IndiceReajuste != IndiceReajusteContrato.Outro
                && c.PeriodicidadeReajusteMeses > 0
                && c.ValorAtual > 0)
            .OrderBy(c => c.Numero).ThenBy(c => c.Descricao)
            .ToListAsync(cancellationToken);

        var contratoIds = contratos.Select(c => c.Id).ToList();
        var comPendente = (await _db.MovimentacoesValorContrato.AsNoTracking()
            .Where(m => contratoIds.Contains(m.ContratoId) && m.Status == StatusMovimentacaoValor.Pendente)
            .Select(m => m.ContratoId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var itens = new List<ReajusteAniversarioItem>();

        foreach (var contrato in contratos.Where(c => !comPendente.Contains(c.Id)))
        {
            for (var ciclo = 0; ciclo < MaximoCiclosPorExecucao; ciclo++)
            {
                var item = await ProcessarAniversarioAsync(empresa, contrato, hoje, cancellationToken);
                if (item is null)
                    break;

                itens.Add(item);

                // Só um Contrato automático continua (atrasado mais de um
                // ciclo); pendente / aguardando índice / atrasado demais param.
                if (item.Resultado != ResultadoReajusteAniversario.AplicadoAutomaticamente)
                    break;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new GeracaoReajustesResponse(itens);
    }

    private async Task<ReajusteAniversarioItem?> ProcessarAniversarioAsync(
        Empresa empresa, Contrato contrato, DateOnly hoje, CancellationToken cancellationToken)
    {
        var aniversario = SituacaoContratoCalculator.CalcularDataProximoReajuste(contrato);
        if (aniversario > hoje)
            return null;

        // Contrato termina antes do aniversário: não há o que reajustar.
        if (contrato.DataFim is { } fim && fim < aniversario)
            return null;

        var indice = contrato.IndiceReajuste!.Value;

        if (aniversario < hoje.AddDays(-DiasMaximosAtraso))
            return new ReajusteAniversarioItem(contrato.Id, NumeroContratoRegras.Rotulo(contrato.Numero, contrato.Descricao), aniversario, indice,
                ResultadoReajusteAniversario.AtrasadoDemais, null, contrato.ValorAtual, null,
                $"Aniversário há mais de {DiasMaximosAtraso} dias — registre o reajuste manualmente no Histórico de valores.");

        var acumulado = await _calcularAcumulado.ExecutarAsync(indice, aniversario, cancellationToken);
        if (!acumulado.Disponivel || acumulado.PercentualAcumulado is not { } percentualCalculado)
            return new ReajusteAniversarioItem(contrato.Id, NumeroContratoRegras.Rotulo(contrato.Numero, contrato.Descricao), aniversario, indice,
                ResultadoReajusteAniversario.AguardandoIndice, null, contrato.ValorAtual, null, acumulado.Mensagem);

        var percentual = ReajusteContrato.PercentualEfetivo(percentualCalculado, contrato.AplicarIndiceNegativo);
        var valorNovo = await ReajusteContrato.CalcularValorNovoAsync(_db, contrato, percentual, cancellationToken);
        var mantidoPorNegativo = percentual != percentualCalculado;

        var evento = new MovimentacaoValorContrato
        {
            ContratoId = contrato.Id,
            Tipo = TipoMovimentacaoValor.Reajuste,
            Status = StatusMovimentacaoValor.Pendente,
            DataVigencia = aniversario,
            ValorAnterior = contrato.ValorAtual,
            ValorNovo = valorNovo,
            PercentualCalculado = percentualCalculado,
            Indice = indice,
            CompetenciaIndiceInicio = acumulado.CompetenciaInicio,
            CompetenciaIndiceFim = acumulado.CompetenciaFim,
            Motivo = mantidoPorNegativo
                ? $"Índice acumulado negativo ({percentualCalculado.ToString("0.####", CultureInfo.GetCultureInfo("pt-BR"))}%): valor mantido, conforme o contrato."
                : null
        };

        // Negativo mantido (0%) não tem o que aprovar: nada muda no valor.
        var aplicarAgora = mantidoPorNegativo || ReajusteContrato.ReajusteAutomaticoEfetivo(contrato, empresa);

        if (aplicarAgora)
        {
            evento.Automatico = true;
            evento.DecididoEm = DateTimeOffset.UtcNow;
            await ReajusteContrato.AplicarAsync(_db, contrato, evento, valorNovo, percentual, cancellationToken);
        }

        _db.MovimentacoesValorContrato.Add(evento);

        _auditLogWriter.Registrar(
            aplicarAgora ? "AplicarReajusteAutomaticoContrato" : "ProporReajusteContrato", "Contrato", contrato.Id,
            new { Aniversario = aniversario, Indice = indice, PercentualCalculado = percentualCalculado, PercentualAplicado = percentual, evento.ValorAnterior, ValorNovo = valorNovo });

        return new ReajusteAniversarioItem(contrato.Id, NumeroContratoRegras.Rotulo(contrato.Numero, contrato.Descricao), aniversario, indice,
            aplicarAgora ? ResultadoReajusteAniversario.AplicadoAutomaticamente : ResultadoReajusteAniversario.AguardandoAprovacao,
            percentualCalculado, evento.ValorAnterior, valorNovo, evento.Motivo);
    }
}
