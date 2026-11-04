using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

/// <summary>
/// Reajuste do ANIVERSÁRIO do Contrato pelo índice: no dia (ou depois, se o
/// índice ainda não saiu), calcula o acumulado de 12 meses e cria o evento
/// no Histórico de valores — aplicado na hora (reajuste automático) ou
/// aguardando aprovação. Roda todo dia pelo job e pelo botão "Verificar
/// reajustes" da tela de Contratos.
/// </summary>
public interface IGerarReajustesAniversarioUseCase
{
    Task<GeracaoReajustesResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken);
}

/// <summary>Aprovar (pelo índice ou negociado) ou recusar um reajuste que está aguardando aprovação.</summary>
public interface IDecidirReajusteUseCase
{
    Task ExecutarAsync(Guid contratoId, Guid movimentacaoId, DecidirReajusteRequest request, CancellationToken cancellationToken);
}

public enum AcaoDecisaoReajuste
{
    /// <summary>Aplica o percentual calculado pelo índice.</summary>
    AplicarCalculado = 1,

    /// <summary>Aplica outro percentual (ou valor) combinado com o cliente — exige motivo.</summary>
    AplicarNegociado = 2,

    /// <summary>Não reajusta neste ciclo — exige motivo. O próximo reajuste fica para o aniversário seguinte.</summary>
    Recusar = 3
}

/// <param name="PercentualNegociado">Só em AplicarNegociado: informe ESTE ou <paramref name="ValorNovoNegociado"/>.</param>
public sealed record DecidirReajusteRequest(
    AcaoDecisaoReajuste Acao,
    decimal? PercentualNegociado,
    decimal? ValorNovoNegociado,
    string? Motivo);

public enum ResultadoReajusteAniversario
{
    AguardandoAprovacao = 1,
    AplicadoAutomaticamente = 2,
    /// <summary>Índice do período ainda não publicado/importado — tenta de novo amanhã.</summary>
    AguardandoIndice = 3,
    /// <summary>Aniversário há mais de <c>DiasMaximosAtraso</c> dias: não se calcula sozinho, registre manualmente.</summary>
    AtrasadoDemais = 4
}

public sealed record ReajusteAniversarioItem(
    Guid ContratoId,
    string Descricao,
    DateOnly Aniversario,
    IndiceReajusteContrato Indice,
    ResultadoReajusteAniversario Resultado,
    decimal? PercentualCalculado,
    decimal? ValorAnterior,
    decimal? ValorNovo,
    string? Detalhe);

public sealed record GeracaoReajustesResponse(IReadOnlyList<ReajusteAniversarioItem> Itens);
