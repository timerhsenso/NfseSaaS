using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

// ---------- Plataforma › Planos › Reajustar assinantes (B2) ----------

/// <summary>Uma assinatura do plano na prévia: entra no reajuste ou por que fica de fora.</summary>
public sealed record LinhaPreviaReajuste(Guid TenantId, string Conta, decimal? PrecoAtual, bool Fundador, bool Entra, string? Motivo);

/// <param name="PrecoNovo">Preço de tabela do plano na data escolhida.</param>
/// <param name="DataMinima">Primeira data aceita (hoje + 30 dias).</param>
public sealed record PreviaReajusteResponse(
    string Plano,
    decimal PrecoNovo,
    DateOnly AplicarEm,
    DateOnly DataMinima,
    int Entram,
    IReadOnlyList<LinhaPreviaReajuste> Contas);

public sealed record AgendarReajusteRequest(DateOnly AplicarEm, string? Motivo);

/// <param name="SemDestinatario">Agendados sem administrador com e-mail confirmado (aviso não saiu).</param>
public sealed record AgendarReajusteResponse(Guid LoteId, int Agendados, int ForaDoReajuste, int SemDestinatario);

public sealed record ReajusteAssinaturaResponse(
    Guid Id,
    Guid LoteId,
    string Conta,
    decimal PrecoAnterior,
    decimal PrecoNovo,
    DateOnly AplicarEm,
    StatusReajusteAssinatura Status,
    DateTimeOffset? AvisadoEm,
    DateTimeOffset? AplicadoEm,
    string? Motivo,
    string? Detalhe,
    DateTimeOffset CriadoEm);

/// <summary>
/// Reajuste do preço de tabela para quem já assina (só operador da
/// plataforma). Toda ação vai para o Histórico do operador.
/// </summary>
public interface IReajusteAssinantesUseCase
{
    Task<PreviaReajusteResponse> PreviaAsync(Guid planoId, DateOnly aplicarEm, CancellationToken cancellationToken);

    /// <summary>Agenda para quem entra e manda o aviso por e-mail na hora.</summary>
    Task<AgendarReajusteResponse> AgendarAsync(Guid planoId, AgendarReajusteRequest request, Guid? operadorId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReajusteAssinaturaResponse>> ListarAsync(Guid planoId, CancellationToken cancellationToken);

    /// <summary>Cancela o que ainda está agendado no lote. Devolve quantos.</summary>
    Task<int> CancelarLoteAsync(Guid loteId, Guid? operadorId, CancellationToken cancellationToken);
}

public sealed record ResultadoReajustesAssinatura(int Aplicados, int Cancelados, int Falhas);

/// <summary>Job diário: aplica no Asaas os reajustes que chegaram na data.</summary>
public interface IAplicarReajustesAssinaturaUseCase
{
    Task<ResultadoReajustesAssinatura> ExecutarAsync(CancellationToken cancellationToken);
}
