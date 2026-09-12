using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Email;

/// <summary>
/// Consome IBackgroundEmailQueue e processa um item por vez — envio
/// serializado, não paralelo, de propósito (evita saturar o servidor
/// SMTP com rajada e mantém a ordem de tentativa próxima da ordem de
/// pedido, mais fácil de acompanhar na tela de E-mails).
///
/// Roda pra sempre em background (BackgroundService), fora do ciclo de
/// vida de qualquer requisição HTTP — por isso cria um escopo de DI novo
/// (e um AppDbContext novo) a cada item, e usa TenantIdOverrideDeSistema
/// pra gravar o resultado (EmailLog é ITenantEntity, e aqui não há
/// ICurrentTenant resolvido por Claim nenhum).
/// </summary>
public sealed class EmailDispatchHostedService : BackgroundService
{
    private readonly IBackgroundEmailQueue _fila;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatchHostedService> _logger;

    public EmailDispatchHostedService(IBackgroundEmailQueue fila, IServiceScopeFactory scopeFactory, ILogger<EmailDispatchHostedService> logger)
    {
        _fila = fila;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _fila.LerTodosAsync(stoppingToken))
        {
            try
            {
                await ProcessarAsync(item, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nunca deixa uma falha inesperada matar o loop inteiro —
                // se acontecesse, todo e-mail depois desse na fila
                // pararia de ser processado pro resto da vida do processo.
                _logger.LogError(ex, "Falha inesperada processando item de e-mail {EmailLogId}.", item.EmailLogId);
            }
        }
    }

    private async Task ProcessarAsync(ItemFilaEmail item, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var log = await db.EmailLogs.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == item.EmailLogId, cancellationToken);
        if (log is null)
            return; // não deveria acontecer — o registro é criado antes de enfileirar

        log.Tentativas++;

        var resultado = await emailSender.EnviarAsync(item.Destinatario, item.Assunto, item.CorpoHtml, cancellationToken);

        if (resultado.Sucesso)
        {
            log.Status = StatusEmailLog.Enviado;
            log.EnviadoEm = DateTimeOffset.UtcNow;
            log.ErroDetalhe = null;
        }
        else
        {
            log.Status = StatusEmailLog.Falhou;
            log.ErroDetalhe = resultado.ErroDetalhe;
        }

        db.TenantIdOverrideDeSistema = log.TenantId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.TenantIdOverrideDeSistema = null;
        }
    }
}
