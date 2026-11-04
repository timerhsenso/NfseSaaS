using NfseSaaS.Infrastructure.Monitoramento;
using Serilog.Core;
using Serilog.Events;

namespace NfseSaaS.Web.Monitoramento;

/// <summary>
/// Sink do Serilog que manda todo Error/Fatal do log para a
/// AlertaOperadorFila (e daí, por e-mail, ao operador da plataforma).
/// Pega erro de qualquer lugar: requisição (ExceptionHandlingMiddleware),
/// jobs (inclusive falha parcial de uma Empresa) e serviços em segundo plano.
///
/// Fica de fora, para não virar laço nem duplicar:
/// - o próprio envio de e-mail e o monitoramento (se o SMTP cair, avisar por
///   e-mail não adianta — fica no log e na tela de E-mails);
/// - o Hangfire em si: a falha definitiva de job já chega pelo
///   AlertaFalhaJobFilter, com um texto claro.
/// </summary>
public sealed class AlertaErroSink : ILogEventSink
{
    private static readonly string[] OrigensIgnoradas =
    {
        "NfseSaaS.Infrastructure.Email",
        "NfseSaaS.Infrastructure.Monitoramento.AlertaOperador",
        "Hangfire"
    };

    private readonly AlertaOperadorFila _fila;

    public AlertaErroSink(AlertaOperadorFila fila)
    {
        _fila = fila;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Error)
            return;

        var origem = Texto(logEvent, "SourceContext");
        if (origem is not null && OrigensIgnoradas.Any(o => origem.StartsWith(o, StringComparison.Ordinal)))
            return;

        // Mesmo modelo de mensagem + mesma origem + mesmo tipo de exceção =
        // o "mesmo erro" para efeito de agrupamento (os valores variam).
        var chave = $"{origem}|{logEvent.MessageTemplate.Text}|{logEvent.Exception?.GetType().FullName}";

        _fila.Publicar(new AlertaOperador(
            logEvent.Timestamp,
            logEvent.Level == LogEventLevel.Fatal ? "FATAL" : "Erro",
            logEvent.RenderMessage(),
            origem,
            logEvent.Exception?.ToString(),
            Texto(logEvent, "CorrelationId"),
            chave));
    }

    private static string? Texto(LogEvent logEvent, string propriedade) =>
        logEvent.Properties.TryGetValue(propriedade, out var valor) && valor is ScalarValue { Value: { } v }
            ? v.ToString()
            : null;
}
