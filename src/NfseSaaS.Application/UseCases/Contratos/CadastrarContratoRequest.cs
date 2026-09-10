using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

/// <summary>
/// ValorAtual não faz mais parte deste DTO — a partir da Fase 6 ele é
/// sempre calculado como a soma de Servicos[].Quantidade*ValorUnitario
/// (ver CadastrarContratoUseCase), nunca informado direto, pra não
/// correr o risco do total divergir das linhas.
/// </summary>
public sealed record CadastrarContratoRequest(
    Guid EmpresaId,
    Guid ClienteId,
    string Descricao,
    IReadOnlyList<ContratoServicoRequest> Servicos,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    IndiceReajusteContrato? IndiceReajuste,
    int? DiasAlertaOverride,
    string? Observacao,
    DateOnly? DataFim,
    TipoCobrancaContrato TipoCobranca,
    bool PermitirAlterarValorNaEmissao);
