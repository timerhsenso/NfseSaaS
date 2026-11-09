using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.DadosConta;

/// <summary>
/// Exportação dos dados da conta (D1, portabilidade da LGPD), tela "Meus dados":
/// 1. o administrador pede (<see cref="SolicitarAsync"/>): grava o pedido e agenda o job;
/// 2. o job monta o ZIP (<see cref="GerarAsync"/>, via <see cref="MontadorExportacaoDados"/>)
///    num arquivo fora do banco e do backup, e avisa quem pediu por e-mail;
/// 3. o administrador baixa logado (<see cref="BaixarAsync"/>), por 7 dias;
/// 4. a limpeza diária (<see cref="LimparAsync"/>) apaga o que expirou.
/// Pedido, download e geração ficam no Histórico da conta.
/// </summary>
public sealed class ExportacaoDadosService : IExportacaoDadosUseCase, IGerarExportacaoDadosUseCase
{
    /// <summary>Pedido parado em Pendente/Gerando por mais que isto é queda no meio (vira Erro).</summary>
    public static readonly TimeSpan TempoMaximoGerando = TimeSpan.FromHours(6);

    private const int QuantidadeNaTela = 10;
    private const string MotivoFalha = "Não foi possível montar o arquivo. Tente de novo; se repetir, fale com o suporte.";
    private const string MotivoInterrompida = "A montagem do arquivo foi interrompida. Peça de novo.";

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IBackgroundJobClient _jobs;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IEmailQueueService _emailQueue;
    private readonly MontadorExportacaoDados _montador;
    private readonly ExportacaoDadosOptions _options;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<ExportacaoDadosService> _logger;

    public ExportacaoDadosService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IBackgroundJobClient jobs,
        IAuditLogWriter auditLogWriter,
        IEmailQueueService emailQueue,
        MontadorExportacaoDados montador,
        IOptions<ExportacaoDadosOptions> options,
        IOptions<EmailOptions> emailOptions,
        ILogger<ExportacaoDadosService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _jobs = jobs;
        _auditLogWriter = auditLogWriter;
        _emailQueue = emailQueue;
        _montador = montador;
        _options = options.Value;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    // ---------- Tela ----------

    public async Task<ExportacoesDadosResponse> ListarAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        var administrador = await EhAdministradorAsync(tenantId, usuarioId, cancellationToken);

        var exportacoes = await _db.ExportacoesDados.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(QuantidadeNaTela)
            .ToListAsync(cancellationToken);

        var motivo = RegrasExportacaoDados.MotivoParaNaoPedir(
            administrador,
            exportacoes.Any(EmAndamento),
            await PedidosHojeAsync(cancellationToken));

        var agora = DateTimeOffset.UtcNow;
        return new ExportacoesDadosResponse(
            administrador,
            motivo,
            RegrasExportacaoDados.DiasValidade,
            administrador ? exportacoes.Select(e => Resposta(e, agora)).ToList() : []);
    }

    public async Task<ExportacaoDadosResponse> SolicitarAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        var administrador = await EhAdministradorAsync(tenantId, usuarioId, cancellationToken);
        var emAndamento = await _db.ExportacoesDados.AsNoTracking()
            .AnyAsync(e => e.Status == StatusExportacaoDados.Pendente || e.Status == StatusExportacaoDados.Gerando, cancellationToken);

        if (RegrasExportacaoDados.MotivoParaNaoPedir(administrador, emAndamento, await PedidosHojeAsync(cancellationToken)) is { } motivo)
            throw new RegraNegocioException(motivo);

        var exportacao = new ExportacaoDados { TenantId = tenantId, SolicitadoPorUsuarioId = usuarioId };
        _db.ExportacoesDados.Add(exportacao);
        _auditLogWriter.Registrar("SolicitarExportacaoDados", "ExportacaoDados", exportacao.Id, new { UsuarioId = usuarioId });
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            _jobs.Enqueue<IExportacaoDadosJob>(job => job.GerarAsync(tenantId, exportacao.Id));
        }
        catch (Exception ex)
        {
            // Sem fila não há como gerar: o pedido não pode ficar "em andamento" para sempre.
            exportacao.Status = StatusExportacaoDados.Erro;
            exportacao.Erro = MotivoFalha;
            exportacao.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Não foi possível agendar a exportação {ExportacaoId} da conta {TenantId}.", exportacao.Id, tenantId);
            throw new RegraNegocioException(MotivoFalha);
        }

        _logger.LogInformation("Exportação dos dados {ExportacaoId} pedida na conta {TenantId}.", exportacao.Id, tenantId);
        return Resposta(exportacao, DateTimeOffset.UtcNow);
    }

    public async Task<ArquivoExportacaoDados> BaixarAsync(Guid exportacaoId, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        if (!await EhAdministradorAsync(tenantId, usuarioId, cancellationToken))
            throw new RegraNegocioException(RegrasExportacaoDados.MotivoSoAdministrador);

        // Filtro de conta: exportação de outra conta não é encontrada.
        var exportacao = await _db.ExportacoesDados.FirstOrDefaultAsync(e => e.Id == exportacaoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Exportação {exportacaoId} não encontrada.");

        var agora = DateTimeOffset.UtcNow;
        var caminho = _options.CaminhoArquivo(tenantId, exportacao.Id);
        if (!RegrasExportacaoDados.PodeBaixar(exportacao.Status, exportacao.ExpiraEm, agora))
            throw new RegraNegocioException(RegrasExportacaoDados.MotivoIndisponivel);

        if (!File.Exists(caminho))
        {
            // Ex.: o servidor foi reiniciado num deploy e a pasta temporária foi limpa.
            exportacao.Status = StatusExportacaoDados.Expirada;
            exportacao.UpdatedAt = agora;
            await _db.SaveChangesAsync(cancellationToken);
            throw new RegraNegocioException(RegrasExportacaoDados.MotivoIndisponivel);
        }

        exportacao.BaixadaEm = agora;
        exportacao.UpdatedAt = agora;
        _auditLogWriter.Registrar("BaixarExportacaoDados", "ExportacaoDados", exportacao.Id, new { UsuarioId = usuarioId, exportacao.TamanhoBytes });
        await _db.SaveChangesAsync(cancellationToken);

        var documento = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Documento).FirstOrDefaultAsync(cancellationToken) ?? "";
        return new ArquivoExportacaoDados(caminho, RegrasExportacaoDados.NomeArquivo(documento, exportacao.ConcluidaEm ?? exportacao.CreatedAt));
    }

    // ---------- Job ----------

    /// <summary>Roda com a conta definida no escopo (ver ExportacaoDadosJob).</summary>
    public async Task GerarAsync(Guid exportacaoId, CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId
            ?? throw new InvalidOperationException("Conta não definida para a exportação dos dados.");

        var exportacao = await _db.ExportacoesDados.FirstOrDefaultAsync(e => e.Id == exportacaoId, cancellationToken);
        if (exportacao is null || !EmAndamento(exportacao))
            return; // Já tratada (job repetido) ou apagada com a conta.

        exportacao.Status = StatusExportacaoDados.Gerando;
        exportacao.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var caminho = _options.CaminhoArquivo(tenantId, exportacao.Id);
        var temporario = caminho + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            int notas;
            await using (var arquivo = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None))
                notas = await _montador.MontarAsync(tenantId, arquivo, cancellationToken);
            File.Move(temporario, caminho, overwrite: true);

            var agora = DateTimeOffset.UtcNow;
            exportacao.Status = StatusExportacaoDados.Pronta;
            exportacao.ConcluidaEm = agora;
            exportacao.ExpiraEm = RegrasExportacaoDados.Expiracao(agora);
            exportacao.TamanhoBytes = new FileInfo(caminho).Length;
            exportacao.QuantidadeNotas = notas;
            exportacao.Erro = null;
            exportacao.UpdatedAt = agora;
            _auditLogWriter.Registrar("GerarExportacaoDados", "ExportacaoDados", exportacao.Id, new { exportacao.TamanhoBytes, Notas = notas });
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            TentarApagar(temporario);
            exportacao.Status = StatusExportacaoDados.Erro;
            exportacao.Erro = MotivoFalha;
            exportacao.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Falha ao montar a exportação {ExportacaoId} da conta {TenantId}.", exportacao.Id, tenantId);
            throw;
        }

        _logger.LogInformation("Exportação dos dados {ExportacaoId} da conta {TenantId} pronta: {Notas} nota(s), {Bytes} bytes.",
            exportacao.Id, tenantId, exportacao.QuantidadeNotas, exportacao.TamanhoBytes);
        await AvisarProntaAsync(exportacao, CancellationToken.None);
    }

    /// <summary>
    /// Todas as contas (job sem conta atual, por isso IgnoreQueryFilters e
    /// ExecuteUpdate): expira o que venceu, encerra pedido parado e apaga
    /// do disco o que não pode mais ser baixado — inclusive pastas de
    /// contas que foram excluídas.
    /// </summary>
    public async Task<ResultadoLimpezaExportacoes> LimparAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var expiradas = await _db.ExportacoesDados.IgnoreQueryFilters()
            .Where(e => e.Status == StatusExportacaoDados.Pronta && e.ExpiraEm <= agora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, StatusExportacaoDados.Expirada)
                .SetProperty(e => e.UpdatedAt, agora), cancellationToken);

        var limiteParada = agora - TempoMaximoGerando;
        await _db.ExportacoesDados.IgnoreQueryFilters()
            .Where(e => (e.Status == StatusExportacaoDados.Pendente || e.Status == StatusExportacaoDados.Gerando)
                        && (e.UpdatedAt ?? e.CreatedAt) < limiteParada)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, StatusExportacaoDados.Erro)
                .SetProperty(e => e.Erro, MotivoInterrompida)
                .SetProperty(e => e.UpdatedAt, agora), cancellationToken);

        var apagados = 0;
        var pastaBase = _options.PastaBase();
        if (Directory.Exists(pastaBase))
        {
            // Só fica no disco o que ainda pode ser baixado ou está sendo montado.
            var vivas = (await _db.ExportacoesDados.IgnoreQueryFilters().AsNoTracking()
                    .Where(e => e.Status == StatusExportacaoDados.Pronta
                                || e.Status == StatusExportacaoDados.Pendente
                                || e.Status == StatusExportacaoDados.Gerando)
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken))
                .Select(id => id.ToString("N"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var arquivo in Directory.EnumerateFiles(pastaBase, "*", SearchOption.AllDirectories))
            {
                var nome = Path.GetFileName(arquivo);
                var id = nome.Split('.')[0];
                if (vivas.Contains(id))
                    continue;
                if (TentarApagar(arquivo))
                    apagados++;
            }

            foreach (var pasta in Directory.EnumerateDirectories(pastaBase))
            {
                if (!Directory.EnumerateFileSystemEntries(pasta).Any())
                    TentarApagarPasta(pasta);
            }
        }

        if (expiradas > 0 || apagados > 0)
            _logger.LogInformation("Exportações dos dados: {Expiradas} expirada(s), {Apagados} arquivo(s) apagado(s).", expiradas, apagados);

        return new ResultadoLimpezaExportacoes(expiradas, apagados);
    }

    // ---------- Apoio ----------

    private async Task AvisarProntaAsync(ExportacaoDados exportacao, CancellationToken cancellationToken)
    {
        try
        {
            var email = await _db.Users.AsNoTracking()
                .Where(u => u.Id == exportacao.SolicitadoPorUsuarioId && u.TenantId == exportacao.TenantId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(email))
                return;

            var baseUrl = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
                ? "https://" + DocumentosLegais.Site
                : _emailOptions.AppBaseUrl.TrimEnd('/');
            var expira = exportacao.ExpiraEm!.Value.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy");
            var corpo = $"""
                <p>Olá,</p>
                <p>A exportação dos dados da sua conta no {DocumentosLegais.NomeServico} está pronta: {exportacao.QuantidadeNotas} nota(s) e todos os cadastros, num arquivo ZIP.</p>
                <p>Para baixar, entre na plataforma e abra <a href="{WebUtility.HtmlEncode(baseUrl)}/MeusDados">Meus dados</a>. O arquivo fica disponível até <strong>{expira}</strong>.</p>
                <p style="color:#64748b;font-size:12px">Por segurança o arquivo não vai anexado: só um administrador da conta, logado, consegue baixar. Se não foi você quem pediu, troque a sua senha e fale com {DocumentosLegais.EmailPrivacidade}.</p>
                """;

            await _emailQueue.EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.ExportacaoDados, exportacao.SolicitadoPorUsuarioId, email.Trim(),
                    "Seus dados estão prontos para baixar", corpo, ChaveDeduplicacao: $"exportacao:{exportacao.Id:N}"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // O arquivo está pronto e aparece na tela: o e-mail é só um aviso.
            _logger.LogWarning(ex, "Não foi possível avisar por e-mail a exportação {ExportacaoId}.", exportacao.Id);
        }
    }

    private async Task<int> PedidosHojeAsync(CancellationToken cancellationToken)
    {
        // Meia-noite de Brasília, em UTC (o Npgsql só grava/compara timestamptz com offset 0).
        var inicioDoDia = new DateTimeOffset(DataBrasilia.Hoje().ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3)).ToUniversalTime();
        return await _db.ExportacoesDados.AsNoTracking().CountAsync(e => e.CreatedAt >= inicioDoDia, cancellationToken);
    }

    private async Task<bool> EhAdministradorAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var grupoId = await _db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
            .Select(u => u.GrupoId)
            .FirstOrDefaultAsync(cancellationToken);
        return grupoId is { } id && await _db.Grupos.AsNoTracking()
            .AnyAsync(g => g.Id == id && g.TenantId == tenantId && g.EhAdministrador, cancellationToken);
    }

    private (Guid TenantId, Guid UsuarioId) Identificar() =>
        _currentTenant.TenantId is { } t && _currentUser.UserId is { } u
            ? (t, u)
            : throw new InvalidOperationException("Conta ou usuário não identificado.");

    private static bool EmAndamento(ExportacaoDados e) =>
        e.Status is StatusExportacaoDados.Pendente or StatusExportacaoDados.Gerando;

    private static ExportacaoDadosResponse Resposta(ExportacaoDados e, DateTimeOffset agora) => new(
        e.Id, e.Status, e.CreatedAt, e.ConcluidaEm, e.ExpiraEm, e.TamanhoBytes, e.QuantidadeNotas, e.Erro,
        RegrasExportacaoDados.PodeBaixar(e.Status, e.ExpiraEm, agora));

    private bool TentarApagar(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo))
                return false;
            File.Delete(arquivo);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível apagar o arquivo de exportação {Arquivo}.", arquivo);
            return false;
        }
    }

    private void TentarApagarPasta(string pasta)
    {
        try
        {
            Directory.Delete(pasta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Pasta de exportação {Pasta} não apagada.", pasta);
        }
    }
}
