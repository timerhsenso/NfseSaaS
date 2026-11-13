using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;
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

    internal async Task ProcessarAsync(ItemFilaEmail item, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var log = await db.EmailLogs.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == item.EmailLogId, cancellationToken);
        if (log is null)
            return; // não deveria acontecer — o registro é criado antes de enfileirar

        log.Tentativas++;

        var resultado = await EnviarAsync(scope.ServiceProvider, emailSender, log, item, cancellationToken);

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

    /// <summary>
    /// E-mail com nota anexada (EmailLog.NfseId): os anexos são gerados
    /// agora, no Tenant da nota (inclusive no "Reenviar" da tela de
    /// E-mails). Falha ao montar os anexos vira falha do envio, registrada
    /// no próprio EmailLog — nunca um e-mail de nota sem a nota.
    /// </summary>
    private async Task<ResultadoEnvioEmail> EnviarAsync(
        IServiceProvider servicos, IEmailSender emailSender, EmailLog log, ItemFilaEmail item, CancellationToken cancellationToken)
    {
        var responderPara = log.ResponderPara;
        var copias = EmailsCopiaCliente.Separar(log.Copias);

        if (log.NfseId is not { } idNota)
        {
            // "Mandar um teste para mim" do modelo: sai com o nome da empresa, como a nota de verdade.
            var remetenteTeste = log.Tipo == TipoEmailCatalogo.NfseTomador
                ? await NomeRemetenteDoTesteAsync(servicos, log.TenantId, cancellationToken)
                : null;
            return responderPara is null && copias.Count == 0 && log.CopiaOcultaFixa is null && remetenteTeste is null
                ? await emailSender.EnviarAsync(item.Destinatario, item.Assunto, item.CorpoHtml, cancellationToken)
                : await emailSender.EnviarAsync(new MensagemEmail(item.Destinatario, item.Assunto, item.CorpoHtml, null, responderPara, copias,
                    log.CopiasOcultas, log.CopiaOcultaFixa, remetenteTeste), cancellationToken);
        }

        IReadOnlyCollection<AnexoEmail> anexos;
        string? remetente;
        try
        {
            servicos.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(log.TenantId);
            anexos = await servicos.GetRequiredService<AnexosNfseEmail>().MontarAsync(idNota, log.DocumentosAnexos, cancellationToken);
            remetente = await NomeRemetenteAsync(servicos, log.TenantId, idNota, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Não foi possível montar os anexos da nota {NfseId} para o e-mail {EmailLogId}.", idNota, item.EmailLogId);
            return new ResultadoEnvioEmail(false, $"Não foi possível anexar a nota: {ex.Message}");
        }

        return await emailSender.EnviarAsync(new MensagemEmail(item.Destinatario, item.Assunto, item.CorpoHtml, anexos, responderPara, copias,
            log.CopiasOcultas, log.CopiaOcultaFixa, remetente), cancellationToken);
    }

    /// <summary>
    /// Teste do modelo (sem nota): a mesma empresa que o teste usa nos dados
    /// de exemplo (a primeira da conta). Ver ModeloEmailNotaService.EnviarTesteAsync.
    /// </summary>
    private static async Task<string?> NomeRemetenteDoTesteAsync(IServiceProvider servicos, Guid tenantId, CancellationToken cancellationToken)
    {
        var db = servicos.GetRequiredService<AppDbContext>();
        var empresa = await db.Empresas.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new { e.NomeFantasia, e.RazaoSocial })
            .FirstOrDefaultAsync(cancellationToken);
        if (empresa is null)
            return null;
        var plataforma = servicos.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value.RemetenteNome;
        return RegrasRemetenteEmail.NomeExibicao(empresa.NomeFantasia, empresa.RazaoSocial, plataforma);
    }

    /// <summary>"RHSENSO via MicoEmpresa": a empresa que emitiu a nota, lida na hora (vale também no "Reenviar").</summary>
    private static async Task<string?> NomeRemetenteAsync(IServiceProvider servicos, Guid tenantId, Guid nfseId, CancellationToken cancellationToken)
    {
        var db = servicos.GetRequiredService<AppDbContext>();
        var empresa = await (
            from n in db.NotasFiscais.AsNoTracking().IgnoreQueryFilters()
            join e in db.Empresas.AsNoTracking().IgnoreQueryFilters() on n.EmpresaId equals e.Id
            where n.Id == nfseId && n.TenantId == tenantId
            select new { e.NomeFantasia, e.RazaoSocial })
            .FirstOrDefaultAsync(cancellationToken);
        if (empresa is null)
            return null;
        var plataforma = servicos.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value.RemetenteNome;
        return RegrasRemetenteEmail.NomeExibicao(empresa.NomeFantasia, empresa.RazaoSocial, plataforma);
    }
}
