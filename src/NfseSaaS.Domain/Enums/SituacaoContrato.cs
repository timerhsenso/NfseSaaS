namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Situação de um Contrato em relação ao prazo de reajuste — NUNCA
/// persistida (é sempre calculada na hora, a partir de
/// DataUltimoReajuste/DataInicioContrato + PeriodicidadeReajusteMeses
/// contra a data de hoje). Ver SituacaoContratoCalculator.
/// </summary>
public enum SituacaoContrato
{
    EmDia = 0,
    VencendoEmBreve = 1,
    Vencido = 2
}
