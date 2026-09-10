namespace NfseSaaS.Application.UseCases.HistoricoContrato;

/// <summary>
/// Um item da timeline de histórico de um Contrato — junta AuditLog
/// (genérico) e ReajusteContrato (específico) numa lista só, ordenada
/// por data. Não é uma entidade nova: é a composição de duas fontes já
/// existentes (decisão do usuário: reaproveitar em vez de criar
/// changelog estruturado).
/// </summary>
public sealed record HistoricoContratoItemResponse(
    DateTimeOffset Data,
    string Origem,
    string Descricao);
