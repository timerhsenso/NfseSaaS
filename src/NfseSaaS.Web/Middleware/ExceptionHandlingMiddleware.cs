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

            if (ex is NfseValidationException nfseValidationException && nfseValidationException.Codigos.Count > 0)
            {
                _logger.LogWarning(
                    "DPS reprovada em validação local: {Erros}",
                    string.Join(" | ", nfseValidationException.Codigos));
            }

            var statusCode = ex switch
            {
                ValidacaoException => HttpStatusCode.UnprocessableEntity,
                RecursoNaoEncontradoException => HttpStatusCode.NotFound,
                RegraNegocioException => HttpStatusCode.UnprocessableEntity,
                NfseValidationException => HttpStatusCode.UnprocessableEntity,
                NfseCertificateException => HttpStatusCode.InternalServerError,
                NfseApiException => HttpStatusCode.BadGateway,
                _ => HttpStatusCode.InternalServerError
            };

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            // ValidacaoException e NfseValidationException sempre incluem os
            // erros específicos (por campo ou por regra de validação da
            // DPS) — não é segredo de infraestrutura, é informação que o
            // próprio cliente da API precisa para corrigir a requisição,
            // então vai em qualquer ambiente (não só Development).
            object payload = ex switch
            {
                ValidacaoException validacaoException => new
                {
                    erro = validacaoException.Message,
                    erros = validacaoException.Erros
                },
                NfseValidationException erroValidacaoDps => new
                {
                    erro = erroValidacaoDps.Message,
                    erros = erroValidacaoDps.Codigos
                },
                _ when _environment.IsDevelopment() => new { erro = ex.Message, detalhe = ex.ToString() },
                _ => new { erro = "Ocorreu um erro ao processar a solicitação.", detalhe = (string?)null }
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
