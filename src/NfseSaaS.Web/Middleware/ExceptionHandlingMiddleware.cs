using System.Net;
using System.Text.Json;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Web.Middleware;

/// <summary>
/// Middleware global de tratamento de exceções. Nunca expõe stack trace ao
/// usuário — registra o detalhe técnico completo no log (com CorrelationId
/// já anexado pelo CorrelationIdMiddleware) e devolve uma resposta JSON
/// genérica e segura.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro não tratado ao processar {Method} {Path}", context.Request.Method, context.Request.Path);

            var statusCode = ex switch
            {
                RecursoNaoEncontradoException => HttpStatusCode.NotFound,
                NfseValidationException => HttpStatusCode.UnprocessableEntity,
                NfseCertificateException => HttpStatusCode.InternalServerError,
                NfseApiException => HttpStatusCode.BadGateway,
                _ => HttpStatusCode.InternalServerError
            };

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            var payload = _environment.IsDevelopment()
                ? new { erro = ex.Message, detalhe = ex.ToString() }
                : new { erro = "Ocorreu um erro ao processar a solicitação.", detalhe = (string?)null };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
