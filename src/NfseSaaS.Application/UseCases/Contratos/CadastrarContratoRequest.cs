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
    // Nome opcional (desde 11/11/2026 o número identifica o contrato).
    string? Descricao,
    IReadOnlyList<ContratoServicoRequest> Servicos,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    IndiceReajusteContrato? IndiceReajuste,
    int? DiasAlertaOverride,
    string? Observacao,
    StatusContrato Status,
    DateOnly? DataFim,
    TipoCobrancaContrato TipoCobranca,
    bool PermitirAlterarValorNaEmissao,
    bool? ReajusteAutomatico = null,
    bool AplicarIndiceNegativo = false,
    // C2: local da prestação (IBGE) padrão das notas. Nulo/em branco = município da Empresa.
    string? CodigoMunicipioPrestacao = null,
    // Número do contrato (texto livre, único na empresa). Nulo/vazio = o próximo "ano/NNNN" da empresa.
    string? Numero = null,
    // IM1: contrato que vem de outro sistema já com reajustes no valor. Nulo = nenhum reajuste ainda.
    DateOnly? DataUltimoReajuste = null);
