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

    builder.Services.AddControllersWithViews(options =>
    {
        // Validação automática (FluentValidation) de todo DTO de entrada
        // que tenha um IValidator<T> registrado — ver AddApplication().
        options.Filters.Add<ValidacaoAutomaticaFilter>();
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

    await NfseSaaS.Infrastructure.Identity.IdentitySeeder.SeedRolesAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.TelaSeeder.SeedTelasAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.GrupoBackfillSeeder.BackfillAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.AdministradorTelaSeeder.SincronizarAsync(app.Services);
    await NfseSaaS.Infrastructure.Persistence.Seeders.EmailPendenteRecuperador.ReenfileirarPendentesAsync(app.Services);

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

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

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
