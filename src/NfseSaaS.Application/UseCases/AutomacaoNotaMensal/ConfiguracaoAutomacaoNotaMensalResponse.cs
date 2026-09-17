using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.AutomacaoNotaMensal;

public sealed record ConfiguracaoAutomacaoNotaMensalResponse(
    bool Ativo,
    FrequenciaAutomacaoNotaMensal Frequencia,
    DayOfWeek? DiaSemana,
    int? DiaDoMes,
    TimeOnly Horario,
    ModoAutomacaoNotaMensal Modo);
