namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Situação de um evento do Histórico de valores. Hoje todo evento nasce
/// Aplicado (registro manual). Pendente/Recusado existem pro reajuste
/// proposto automaticamente no aniversário do Contrato, que espera a
/// decisão do usuário. Começa em 1 pelo mesmo motivo de TipoMovimentacaoValor.
/// </summary>
public enum StatusMovimentacaoValor
{
    Aplicado = 1,
    Pendente = 2,
    Recusado = 3
}
