using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Monitoramento;

/// <summary>
/// Transforma os alertas da AlertaOperadorFila em e-mail para o(s)
/// operador(es) da plataforma (Plataforma:UsuariosOperadoresIds), passando
/// pelo LimitadorAlertas. O e-mail entra na fila normal de e-mails, na conta
/// do próprio operador — aparece na tela de E-mails dele, como qualquer outro.
///
/// Sem operador configurado, os alertas são descartados (o log em arquivo
/// continua com tudo). Qualquer falha aqui só vai pro log com esta categoria,
/// que o sink de alertas ignora — senão um problema no envio de alerta
/// geraria outro alerta, em laço.
/// </summary>
public sealed class AlertaOperadorHostedService : BackgroundService
{
    private const int TamanhoMaximoTitulo = 120;
    private const int TamanhoMaximoExcecao = 6000;

    private readonly AlertaOperadorFila _fila;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlataformaOptions _plataforma;
    private readonly IHostEnvironment _ambiente;
    private readonly ILogger<AlertaOperadorHostedService> _logger;
    private readonly LimitadorAlertas _limitador = new();

    public AlertaOperadorHostedService(
        AlertaOperadorFila fila,
        IServiceScopeFactory scopeFactory,
        IOptions<PlataformaOptions> plataforma,
        IHostEnvironment ambiente,
        ILogger<AlertaOperadorHostedService> logger)
    {
        _fila = fila;
        _scopeFactory = scopeFactory;
        _plataforma = plataforma.Value;
        _ambiente = ambiente;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var alerta in _fila.LerTodosAsync(stoppingToken))
        {
            if (_plataforma.UsuariosOperadoresIds.Count == 0)
                continue;

            var (enviar, suprimidos) = _limitador.Avaliar(alerta.ChaveAgrupamento, alerta.Quando);
            if (!enviar)
                continue;

            try
            {
                await EnviarAsync(alerta, suprimidos, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Não foi possível enfileirar o alerta do operador: {Titulo}", alerta.Titulo);
            }
        }
    }

    private async Task EnviarAsync(AlertaOperador alerta, int suprimidos, CancellationToken cancellationToken)
    {
        List<(string Email, Guid TenantId)> operadores;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            operadores = (await db.Users.AsNoTracking()
                    .Where(u => _plataforma.UsuariosOperadoresIds.Contains(u.Id) && u.Email != null)
                    .Select(u => new { u.Email, u.TenantId })
                    .ToListAsync(cancellationToken))
                .Select(u => (u.Email!, u.TenantId))
                .ToList();
        }

        if (operadores.Count == 0)
            return;

        var (assunto, corpoHtml) = Montar(alerta, suprimidos);

        foreach (var (email, tenantId) in operadores)
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenantId);

            await scope.ServiceProvider.GetRequiredService<IEmailQueueService>().EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.AlertaOperador, null, email, assunto, corpoHtml),
                cancellationToken);
        }
    }

    private (string Assunto, string CorpoHtml) Montar(AlertaOperador alerta, int suprimidos)
    {
        var titulo = alerta.Titulo.ReplaceLineEndings(" ");
        if (titulo.Length > TamanhoMaximoTitulo)
            titulo = titulo[..TamanhoMaximoTitulo] + "…";

        var ambiente = _ambiente.IsProduction() ? "" : $"[{_ambiente.EnvironmentName}] ";
        var assunto = $"{ambiente}[micoempresa] {alerta.Nivel}: {titulo}";

        var excecao = alerta.Excecao;
        if (excecao is { Length: > TamanhoMaximoExcecao })
            excecao = excecao[..TamanhoMaximoExcecao] + "\n…(cortado — veja o log completo)";

        var quando = alerta.Quando.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm:ss");

        var corpoHtml = $"""
            <p><strong>{WebUtility.HtmlEncode(alerta.Titulo)}</strong></p>
            <table cellpadding="4" style="border-collapse:collapse;font-size:13px">
                <tr><td>Quando</td><td>{quando} (Brasília)</td></tr>
                <tr><td>Nível</td><td>{WebUtility.HtmlEncode(alerta.Nivel)}</td></tr>
                {(alerta.Origem is null ? "" : $"<tr><td>Origem</td><td>{WebUtility.HtmlEncode(alerta.Origem)}</td></tr>")}
                {(alerta.CorrelationId is null ? "" : $"<tr><td>CorrelationId</td><td>{WebUtility.HtmlEncode(alerta.CorrelationId)}</td></tr>")}
            </table>
            {(suprimidos > 0 ? $"<p>Este mesmo problema aconteceu mais <strong>{suprimidos}</strong> vez(es) desde o alerta anterior (sem e-mail, para não lotar a caixa).</p>" : "")}
            {(excecao is null ? "" : $"<pre style=\"font-size:12px;white-space:pre-wrap;background:#f1f5f9;padding:8px\">{WebUtility.HtmlEncode(excecao)}</pre>")}
            <p style="color:#64748b;font-size:12px">Repetições do mesmo erro geram no máximo 1 e-mail a cada {LimitadorAlertas.JanelaPorChave.TotalMinutes:0} minutos. Detalhes completos na tela Plataforma › Logs (busque pelo CorrelationId).</p>
            """;

        return (assunto, corpoHtml);
    }
}
