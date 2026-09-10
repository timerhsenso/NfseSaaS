namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Status do ciclo de vida comercial do Contrato — persistido, diferente
/// de SituacaoContrato (que é sempre calculada e trata só do prazo de
/// reajuste). Fase 6 do roadmap.
/// </summary>
public enum StatusContrato
{
    Rascunho = 0,
    Ativo = 1,
    Suspenso = 2,
    Encerrado = 3,
    Cancelado = 4
}
