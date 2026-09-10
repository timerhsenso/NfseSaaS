namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Como o Contrato é cobrado — usado pela Fase 7 (Nota Mensal) pra saber
/// quais Contratos entram na auto-seleção de emissão em lote. Fase 6 do
/// roadmap.
/// </summary>
public enum TipoCobrancaContrato
{
    Avulso = 0,
    Mensal = 1
}
