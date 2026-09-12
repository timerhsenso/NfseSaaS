using System.Net;
using System.Text.Json;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Web.Middleware;

/// <summary>
/// Middleware global de tratamento de exceções — ÚNICO ponto que loga
/// exceção não tratada (_logger.LogError(ex, ...), com CorrelationId já
/// anexado pelo CorrelationIdMiddleware). Nunca expõe stack trace ao
/// usuário: devolve JSON genérico pra requisições de API e redireciona pra
/// /Home/Error (página HTML amigável) pra requisições de tela.
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

            // A resposta já começou a ser escrita (ex.: exceção estourou no
            // meio da renderização de uma View Razor) — não dá mais pra
            // trocar status code/headers. Só loga (já feito acima) e sai;
            // tentar escrever de novo geraria uma segunda exceção que
            // mascararia esta no log.
            if (context.Response.HasStarted)
            {
                return;
            }

            var correlationId = context.TraceIdentifier;

            // Tela MVC (Razor) → devolve uma página de erro amigável em vez
            // de um JSON cru na tela. Só é considerado "tela" quando NÃO é
            // uma chamada sob /api E o cliente aceita HTML (evita
            // classificar chamadas AJAX/fetch feitas de dentro das telas,
            // que também esperam JSON, como se fossem navegação de página).
            var ehRequisicaoDeTela = !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && context.Request.Headers.Accept.Any(h => h != null && h.Contains("text/html", StringComparison.OrdinalIgnoreCase));

            if (ehRequisicaoDeTela)
            {
                context.Response.Redirect($"/Home/Error?cid={Uri.EscapeDataString(correlationId)}");
                return;
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
