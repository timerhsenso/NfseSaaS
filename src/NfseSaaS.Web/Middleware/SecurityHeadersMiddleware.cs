namespace NfseSaaS.Web.Middleware;

/// <summary>
/// Cabeçalhos de segurança aplicados a toda resposta (telas e API).
///
/// script-src SEM 'unsafe-inline': todo &lt;script&gt; inline que existia
/// (Login, Registrar, EsqueciSenha, RedefinirSenha, AceitarConvite,
/// _Layout, Home, Sobre, Grupos) foi movido pra wwwroot/js/ — os poucos
/// valores que vinham do Razor (returnUrl, nome do usuário, permissões
/// de Grupo) agora chegam via atributo data-* no HTML, lido pelo JS
/// externo com .dataset, em vez de interpolados direto no script.
///
/// style-src AINDA usa 'unsafe-inline', de propósito: o Bootstrap seta
/// style inline via JS em tempo de execução (posicionamento de modal,
/// etc.) — isso não é Razor, é o próprio Bootstrap rodando no
/// navegador, e CSP não tem nonce pra atributo style=""/.style.x=y, só
/// pra tag &lt;style&gt;. Tirar isso exigiria trocar de biblioteca de UI;
/// injeção de CSS é um vetor bem mais fraco que injeção de JS, então é
/// uma concessão aceita.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' https://cdn.jsdelivr.net https://code.jquery.com https://cdn.datatables.net https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://cdn.datatables.net; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "img-src 'self' data:; " +
        // cdn.datatables.net: o DataTables carrega o arquivo de i18n
        // (tradução pt-BR) via AJAX (language.url), não via <script> —
        // por isso precisa estar aqui em connect-src, não só em
        // script-src. Sem isso, TODA tela com DataTables mostra
        // "i18n file loading error" (achado depois do deploy, pela
        // primeira vez que alguém abriu uma tela com tabela).
        "connect-src 'self' https://cdn.datatables.net; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting (não escrita direta no dicionário antes de _next) pra
        // garantir que os headers saem em toda resposta, mesmo quando ela
        // é gerada mais adiante no pipeline (ex.: ExceptionHandlingMiddleware
        // reescrevendo status/corpo em caso de erro).
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return Task.CompletedTask;
        });

        return _next(context);
    }
}
