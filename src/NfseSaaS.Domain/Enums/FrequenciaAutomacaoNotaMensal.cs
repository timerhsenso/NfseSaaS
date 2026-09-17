namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Frequência da automação de Nota Mensal (ver ConfiguracaoAutomacaoNotaMensal).
/// Controla quais campos da configuração são obrigatórios: Semanal exige
/// DiaSemana, Mensal exige DiaDoMes, Diaria não usa nenhum dos dois (só
/// o Horario).
/// </summary>
public enum FrequenciaAutomacaoNotaMensal
{
    Diaria = 0,
    Semanal = 1,
    Mensal = 2
}
