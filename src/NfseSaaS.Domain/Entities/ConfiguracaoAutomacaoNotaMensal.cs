using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Configuração da automação de Nota Mensal — uma linha por Empresa
/// (1:1, EmpresaId único). Só os dados; o agendamento em si (calcular
/// "próxima execução", disparar o job) fica com o motor de scheduling
/// (Hangfire) — esta entidade é lida por ele, não implementa nenhum
/// cálculo de data.
///
/// CompetenciasSemConfirmacao, DesligadoPorInatividade e
/// UltimaCompetenciaAvaliada são escritos só pelo job (modo
/// ListarParaRevisao) — ver ExecutarAutomacaoNotaMensalJob. No modo
/// Automatico não existe "confirmação" pendente (a nota já sai
/// emitida sozinha), então esses três campos não se aplicam e ficam
/// parados em 0/false/null.
/// </summary>
public sealed class ConfiguracaoAutomacaoNotaMensal : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public bool Ativo { get; set; }

    public FrequenciaAutomacaoNotaMensal Frequencia { get; set; }

    /// <summary>Obrigatório quando Frequencia = Semanal; null nos outros casos.</summary>
    public DayOfWeek? DiaSemana { get; set; }

    /// <summary>Obrigatório quando Frequencia = Mensal; 1-28 (nunca 29-31, pra não pular meses sem esse dia); null nos outros casos.</summary>
    public int? DiaDoMes { get; set; }

    public TimeOnly Horario { get; set; }

    public ModoAutomacaoNotaMensal Modo { get; set; }

    /// <summary>
    /// Quantas competências SEGUIDAS (modo ListarParaRevisao) foram
    /// avisadas por e-mail sem nenhuma nota Autorizada emitida. Zera
    /// assim que uma competência tem confirmação; ao chegar em 2,
    /// desliga a automação sozinha (ver DesligadoPorInatividade).
    /// </summary>
    public int CompetenciasSemConfirmacao { get; set; }

    /// <summary>
    /// true quando o próprio job desligou a automação (Ativo=false) por
    /// 2 competências seguidas sem confirmação — distinto de um usuário
    /// ter desligado manualmente. A tela usa isto pra explicar o motivo
    /// em vez de deixar o switch desligado sem explicação nenhuma.
    /// Reativar (ligar o switch e salvar) zera este campo de novo — ver
    /// AtualizarConfiguracaoAutomacaoNotaMensalUseCase.
    /// </summary>
    public bool DesligadoPorInatividade { get; set; }

    /// <summary>
    /// Último mês (dia 1) que o job já avaliou pra fins do contador
    /// acima — existe só pra não contar a mesma competência mais de uma
    /// vez quando a Frequencia é Diária/Semanal e o job roda várias
    /// vezes dentro do mesmo mês. Null = nunca avaliado ainda (Empresa
    /// nova na automação, ou acabou de ser reativada).
    /// </summary>
    public DateOnly? UltimaCompetenciaAvaliada { get; set; }
}
