namespace NfseSaaS.Domain.Enums;

/// <summary>
/// DOC4: de onde sai o documento — decide qual link oficial a empresa vê.
/// Nacional: o link geral do catálogo (Receita, Caixa, TST). Estadual: o
/// link da UF da empresa. Municipal: o link do município da empresa.
/// Valor numérico gravado no banco: não renumerar.
/// </summary>
public enum AbrangenciaDocumento
{
    Nacional = 1,
    Estadual = 2,
    Municipal = 3
}
