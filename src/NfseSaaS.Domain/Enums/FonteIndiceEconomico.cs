namespace NfseSaaS.Domain.Enums;

/// <summary>
/// De onde veio a variação mensal de um <see cref="Entities.IndiceEconomicoMensal"/>.
/// Começa em 1 (não 0) pelo mesmo motivo dos enums do histórico de valores:
/// o default do CLR nunca é confundido com um valor de verdade.
/// </summary>
public enum FonteIndiceEconomico
{
    /// <summary>Importado automaticamente da API SGS do Banco Central.</summary>
    BancoCentral = 1,

    /// <summary>Lançado (ou corrigido) à mão pelo operador da plataforma — a importação automática nunca sobrescreve.</summary>
    Manual = 2
}
