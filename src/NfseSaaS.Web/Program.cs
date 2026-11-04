using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application;
using NfseSaaS.Infrastructure;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional;
using NfseSaaS.Web.Filters;
using NfseSaaS.Web.HealthChecks;
using NfseSaaS.Web.Middleware;
using Serilog;

// Bootstrap logger: captura qualquer falha durante a própria inicialização
// (antes do host/DI estarem prontos), conforme prática recomendada do Serilog.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        // Error/Fatal também vira e-mail ao operador da plataforma
        // (agrupado e limitado) — ver AlertaErroSink.
        .WriteTo.Sink(new NfseSaaS.Web.Monitoramento.AlertaErroSink(
            services.GetRequiredService<NfseSaaS.Infrastructure.Monitoramento.AlertaOperadorFila>()))
        .WriteTo.Console(outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({CorrelationId}) {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "log-.txt"),
            rollingInterval: RollingInterval.Day,
            outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({CorrelationId}) {Message:lj}{NewLine}{Exception}"));

    // --- Program.cs enxuto: cada camada registra a si mesma (Requisito 16) ---
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddNfseNacional(builder.Configuration);

    // Resolve o IP real do cliente a partir de X-Forwarded-For — em
    // produção a app só é alcançada através do Nginx (nunca direto da
    // internet, ver firewall/infra), então "confiar em qualquer proxy"
    // aqui é uma decisão aceitável: quem quer que esteja mandando esse
    // header já passou pelo único ponto de entrada possível. Sem isto,
    // o rate limiter abaixo enxergaria sempre o IP do Nginx — ou seja,
    // TODO MUNDO compartilhando o mesmo balde de requisições. Se um dia
    // existir mais de um proxy na frente (CDN, LB), isso precisa ser
    // restrito a KnownProxies/KnownNetworks específicos.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Rate limiting por IP em rotas sensíveis. O lockout do Identity (ver
    // AddIdentity abaixo) já trava a CONTA depois de 5 tentativas erradas
    // — mas não limita quantas tentativas um único IP faz contra contas
    // DIFERENTES, nem o volume bruto de requisições (DoS). Cada política
    // é aplicada via [EnableRateLimiting("Nome")] na action específica
    // (ver AuthController/NotaMensalController); o limite global cobre
    // qualquer outra rota como rede de segurança.
    static string ObterChaveDeIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = (context, cancellationToken) =>
        {
            context.HttpContext.Response.ContentType = "application/json";
            return new ValueTask(context.HttpContext.Response.WriteAsync(
                """{"erro":"Muitas requisições. Aguarde um momento e tente novamente."}""",
                cancellationToken));
        };

        // Login/Registro/Redefinição de senha/Aceite de convite — rotas
        // anônimas mais visadas por brute-force/scripting.
        options.AddPolicy("AuthSensivel", context =>
            RateLimitPartition.GetFixedWindowLimiter(ObterChaveDeIp(context), _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 15,
                QueueLimit = 0
            }));

        // Solicitação de recuperação de senha — dispara e-mail; limite
        // mais apertado pra não virar ferramenta de spam/email-bombing
        // contra terceiros (o e-mail informado nem precisa existir).
        options.AddPolicy("Recuperacao", context =>
            RateLimitPartition.GetFixedWindowLimiter(ObterChaveDeIp(context), _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(5),
                PermitLimit = 5,
                QueueLimit = 0
            }));

        // Nota Mensal em lote — operação cara (chama a SEFIN por nota
        // emitida). Uso normal é "uma vez por Empresa por competência";
        // este limite só evita clique duplicado/automação acidental.
        options.AddPolicy("NotaMensal", context =>
            RateLimitPartition.GetFixedWindowLimiter(ObterChaveDeIp(context), _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 3,
                QueueLimit = 0
            }));

        // Limite global (qualquer outra rota) — bem generoso de propósito:
        // telas com DataTables fazem várias chamadas por carregamento, e
        // vários usuários podem estar atrás do mesmo IP corporativo (NAT).
        // É só uma rede de segurança contra flood/scraping, não uma
        // proteção fina por rota.
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(ObterChaveDeIp(context), _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 300,
                QueueLimit = 0
            }));
    });

    // Anti-CSRF: cookie "de validação" HttpOnly (nome fixo, só pra ficar
    // fácil de identificar em DevTools) + header X-CSRF-TOKEN, conferidos
    // em toda action de método não seguro por ValidacaoAntiforgeryFilter
    // (ver Filters/). O par completo — este cookie mais o token que o JS
    // consegue ler e ecoar no header — é emitido a cada resposta pelo
    // middleware registrado logo após app.UseRouting(), abaixo.
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = "NfseSaaS.Antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.HeaderName = "X-CSRF-TOKEN";
    });

    builder.Services.AddControllersWithViews(options =>
    {
        // Validação automática (FluentValidation) de todo DTO de entrada
        // que tenha um IValidator<T> registrado — ver AddApplication().
        options.Filters.Add<ValidacaoAutomaticaFilter>();

        // Anti-CSRF automático em toda action de método não seguro — ver
        // ValidacaoAntiforgeryFilter.
        options.Filters.Add<ValidacaoAntiforgeryFilter>();

        // Usuário logado sem aceite da versão vigente dos Termos de uso e
        // da Política de privacidade vai para a tela de aceite (API: 403
        // "termos_pendentes") — ver ExigeAceiteTermosFilter.
        options.Filters.Add<ExigeAceiteTermosFilter>();
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services
        .AddHealthChecks()
        .AddCheck<PostgresHealthCheck>("postgresql");

    // Leitor dos arquivos de logs/log-.txt (Serilog File sink) — usado
    // pela tela de Logs do sistema. Ferramenta operacional do Web, não
    // atravessa Application/Infrastructure (mesmo raciocínio do
    // PostgresHealthCheck).
    builder.Services.AddSingleton<NfseSaaS.Web.Services.ILogFileReader, NfseSaaS.Web.Services.LogFileReader>();

    // Versão em produção (APP_VERSION/APP_COMMIT/APP_DEPLOY_EM, injetadas
    // no deploy) + CHANGELOG.md — usado pela tela Sobre.
    builder.Services.AddSingleton<NfseSaaS.Web.Services.IVersaoInfo, NfseSaaS.Web.Services.VersaoInfo>();

    var app = builder.Build();

    // Chaves do Data Protection (abrem os certificados de TODOS os clientes
    // e assinam o cookie de login) — ver ProtecaoChavesOptions.
    var protecaoChaves = app.Services.GetRequiredService<NfseSaaS.Infrastructure.Certificates.ProtecaoChavesStatus>();
    if (string.IsNullOrWhiteSpace(protecaoChaves.CaminhoChaves))
    {
        // Sem pasta configurada, o ASP.NET usa a pasta PADRÃO dele
        // (Linux: ~/.aspnet/DataProtection-Keys; Windows/IIS: perfil do
        // usuário ou registro). Funciona, mas a localização fica implícita
        // — e é ela que precisa estar num volume persistente e no backup.
        app.Logger.LogWarning(
            "Chaves do Data Protection na pasta PADRÃO do ASP.NET (Linux: ~/.aspnet/DataProtection-Keys), protegidas por {Mecanismo}. Confirme que essa pasta está num volume persistente e no backup, ou defina DataProtection:KeysPath.",
            protecaoChaves.MecanismoCriptografia);
    }
    else if (!protecaoChaves.ChavesCriptografadas)
    {
        app.Logger.LogWarning(
            "Chaves do Data Protection gravadas SEM criptografia em {CaminhoChaves}. Configure DataProtection:CertificadoPfxPath/CertificadoSenha para criptografá-las.",
            protecaoChaves.CaminhoChaves);
    }
    else
    {
        app.Logger.LogInformation("Chaves do Data Protection em {CaminhoChaves}, protegidas por {Mecanismo}.",
            protecaoChaves.CaminhoChaves, protecaoChaves.MecanismoCriptografia);

        // O certificado só protege as chaves NOVAS: as gravadas antes dele
        // continuam em texto aberto até o operador usar "Proteger chaves
        // antigas" (Plataforma). Erro de leitura aqui não pode impedir a subida.
        try
        {
            using var escopo = app.Services.CreateScope();
            var situacao = escopo.ServiceProvider.GetRequiredService<NfseSaaS.Infrastructure.Certificates.ProtecaoChavesService>().Consultar();
            if (situacao.ChavesSemCriptografia > 0)
                app.Logger.LogWarning(
                    "{Quantidade} chave(s) antiga(s) do Data Protection ainda sem criptografia em {CaminhoChaves}. Use Plataforma › Contas › Proteção das chaves.",
                    situacao.ChavesSemCriptografia, protecaoChaves.CaminhoChaves);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Não foi possível conferir as chaves antigas do Data Protection em {CaminhoChaves}.", protecaoChaves.CaminhoChaves);
        }
    }

    // Os textos de Termos de uso e Política de privacidade (Views/Legal)
    // mostram o responsável pela plataforma — não pode ir pro ar com o
    // marcador "[...]" sem ninguém perceber.
    if (NfseSaaS.Domain.Legal.DocumentosLegais.PossuiMarcadorPendente)
    {
        app.Logger.LogWarning(
            "Termos de uso/Política de privacidade com dado do responsável ainda por preencher ({Responsavel}). Ajuste NfseSaaS.Domain.Legal.DocumentosLegais.",
            NfseSaaS.Domain.Legal.DocumentosLegais.ResponsavelNome);
    }

    await NfseSaaS.Infrastructure.Identity.IdentitySeeder.SeedRolesAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.TelaSeeder.SeedTelasAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.GrupoBackfillSeeder.BackfillAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.AdministradorTelaSeeder.SincronizarAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.EmailPendenteRecuperador.ReenfileirarPendentesAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.CodigoTributacaoNacionalSeeder.SeedAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.CodigoNbsSeeder.SeedAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.AutomacaoNotaMensalJobSincronizador.SincronizarAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.IndicesEconomicosJobSincronizador.SincronizarAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.AvisoCertificadoJobSincronizador.SincronizarAsync(app.Services);

    app.UseForwardedHeaders();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        // NÃO usar app.UseExceptionHandler("/Home/Error") aqui: essa
        // middleware é INTERNA ao pipeline (registrada depois de
        // ExceptionHandlingMiddleware, ou seja, mais perto do endpoint) e
        // captura a exceção ANTES dela chegar no ExceptionHandlingMiddleware
        // — que é quem de fato chama _logger.LogError(ex, ...) com a
        // exceção completa. Resultado prático que estava acontecendo em
        // produção: toda exceção era silenciosamente engolida pelo
        // UseExceptionHandler, que tentava reexecutar a pipeline contra
        // "/Home/Error" (rota que nem existia) e devolvia um 404 em
        // branco pro usuário — e a exceção original nunca era logada,
        // só o log de auditoria ("tentou fazer X") gravado antes dela
        // estourar. ExceptionHandlingMiddleware sozinho já cobre log +
        // status code + resposta amigável (JSON pra /api, HTML pra tela)
        // — não precisa de handler duplicado.
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    // Emite, em toda resposta, o cookie LEGÍVEL por JS que carrega o
    // RequestToken anti-CSRF (o cookie "de validação" em si, HttpOnly, é
    // emitido junto por GetAndStoreTokens — configurado acima em
    // AddAntiforgery). O JS lê este cookie e ecoa o valor no header
    // X-CSRF-TOKEN em todo fetch que muda estado — ver
    // obterCsrfHeader() em wwwroot/js/site.js e
    // ValidacaoAntiforgeryFilter, que confere os dois no backend.
    //
    // PRECISA vir depois de UseAuthentication/UseAuthorization, não
    // antes: o Antiforgery do ASP.NET Core amarra o token gerado à
    // identidade de HttpContext.User NO MOMENTO DA GERAÇÃO. Gerado antes
    // da autenticação, o token sempre fica preso a "usuário anônimo" —
    // e a validação (que roda mais adiante, no filtro, já com o usuário
    // autenticado de verdade) rejeita com "token was meant for a
    // different claims-based user than the current user". Com o
    // middleware aqui, geração e validação enxergam a MESMA identidade
    // dentro da mesma requisição.
    app.Use(async (context, next) =>
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.Cookies.Append("NfseSaaS.Xsrf-Token", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            IsEssential = true
        });

        await next();
    });

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    // /hangfire — dashboard da automação de Nota Mensal (histórico de
    // execuções, "rodar agora"). Autorização própria do Hangfire, não
    // [RequerPermissao] (isto é middleware, não passa pelo pipeline de
    // MVC) — ver HangfireDashboardAuthorizationFilter.
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireDashboardAuthorizationFilter() }
    });

    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "NfseSaaS.Web encerrado de forma inesperada durante a inicialização.");
}
finally
{
    Log.CloseAndFlush();
}

// Necessário para que NfseSaaS.IntegrationTests consiga usar
// WebApplicationFactory<Program> (top-level statements geram uma classe
// Program implicitamente internal; esta declaração a torna pública).
public partial class Program { }
