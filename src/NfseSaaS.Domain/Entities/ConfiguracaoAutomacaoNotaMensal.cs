using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Configuração da automação de Nota Mensal — uma linha por Empresa
/// (1:1, EmpresaId único). Só os dados; o agendamento em si (calcular
/// "próxima execução", disparar o job) fica com o motor de scheduling
/// (Hangfire, adicionado numa fase seguinte) — esta entidade é lida por
/// ele, não implementa nenhum cálculo de data.
///
/// CompetenciasSemConfirmacao e DesligadoPorInatividade existem no
/// schema desde já, mas ainda não são escritos por ninguém nesta fase
/// (isso é Fase 6 do desenho da automação — o contador de "2
/// competências seguidas sem confirmação no modo ListarParaRevisao").
/// Ficam default (0 / false) até lá.
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

    /// <summary>Ver comentário da classe — não escrito nesta fase.</summary>
    public int CompetenciasSemConfirmacao { get; set; }

    /// <summary>Ver comentário da classe — não escrito nesta fase.</summary>
    public bool DesligadoPorInatividade { get; set; }
}
