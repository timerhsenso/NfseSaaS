namespace NfseSaaS.Application.UseCases.Contratos;

/// <summary>
/// Cliente/Empresa não fazem parte deste DTO (não editáveis — trocar o
/// vínculo de um Contrato depois de criado não faz sentido de negócio;
/// é outro Contrato). ValorAtual/DataUltimoReajuste também ficam de fora
/// DE PROPÓSITO: editar o valor aqui pularia o histórico de reajustes
/// (ContratoReajuste, Parte 3) — a única forma prevista de mudar o valor
/// é pelo fluxo dedicado de "Registrar reajuste".
/// </summary>
public sealed record AtualizarContratoRequest(
    Guid ServicoId,
    string Descricao,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    string? IndiceReajuste,
    int? DiasAlertaOverride);
