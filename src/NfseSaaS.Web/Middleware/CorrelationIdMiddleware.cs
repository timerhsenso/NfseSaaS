using Serilog.Context;

namespace NfseSaaS.Web.Middleware;

/// <summary>
/// Garante que toda requisição HTTP tenha um CorrelationId (recebido via
/// header X-Correlation-Id ou gerado), devolvido na resposta e injetado no
/// contexto de log do Serilog (LogContext) — assim ele aparece em todas as
/// linhas de log geradas durante o processamento da requisição, permitindo
/// futuramente rastrear usuário → requisição → DPS → POST SEFIN → resposta.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
