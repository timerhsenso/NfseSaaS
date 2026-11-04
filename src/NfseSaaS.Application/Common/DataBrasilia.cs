namespace NfseSaaS.Application.Common;

/// <summary>
/// "Hoje" no horário de Brasília (-03:00, sem horário de verão desde 2019).
/// O servidor de produção roda em UTC: DateTime.Today/UtcNow viram o dia
/// seguinte depois das 21h — e uma vigência "de hoje" digitada às 22h
/// seria tratada como futura.
/// </summary>
public static class DataBrasilia
{
    public static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
}
