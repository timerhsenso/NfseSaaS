using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;

namespace NfseSaaS.Infrastructure.DadosConta;

/// <summary>
/// Encerramento da conta (D2, parte 1) e expurgo dos dados:
/// - o administrador encerra pela tela "Meus dados", com código de 6
///   números enviado DIRETO pelo SMTP (não fica no EmailLog), como a
///   exclusão de conta do operador; o operador encerra por Plataforma › Contas;
/// - encerrar = cancela a assinatura no Asaas (se houver), fecha o acesso
///   (Ativo=false, sessões derrubadas) e marca a data do expurgo (90 dias);
///   os administradores recebem um e-mail. Até o expurgo, o operador pode
///   reabrir (Reativar);
/// - o job diário (<see cref="ExecutarAsync"/>) apaga: conta SEM notas →
///   tudo (ExclusaoContaService); conta COM notas → tudo menos os documentos
///   fiscais (notas, eventos, empresas, tomadores das notas sem contato),
///   cobrança (faturas, 5 anos) e aceites dos termos (defesa), que ficam.
/// </summary>
public sealed class EncerramentoContaService : IEncerrarContaUseCase, IExpurgarContasEncerradasUseCase
{
    private sealed record CodigoPendente(byte[] Hash, DateTimeOffset ExpiraEm)
    {
        public int Tentativas { get; set; }
    }

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IMemoryCache _cache;
    private readonly IEmailSender _emailSender;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IGatewayCobranca _gateway;
    private readonly ExclusaoContaService _exclusao;
    private readonly CertificateFileStore _certificados;
    private readonly ContratoDocumentoFileStore _documentos;
    private readonly IRecurringJobManager _jobs;
    private readonly PlataformaOptions _plataforma;
    private readonly ILogger<EncerramentoContaService> _logger;

    public EncerramentoContaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IMemoryCache cache,
        IEmailSender emailSender,
        IEmailQueueService emailQueue,
        IAuditLogWriter auditLogWriter,
        IGatewayCobranca gateway,
        ExclusaoContaService exclusao,
        CertificateFileStore certificados,
        ContratoDocumentoFileStore documentos,
        IRecurringJobManager jobs,
        IOptions<PlataformaOptions> plataforma,
        ILogger<EncerramentoContaService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _cache = cache;
        _emailSender = emailSender;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
        _gateway = gateway;
        _exclusao = exclusao;
        _certificados = certificados;
        _documentos = documentos;
        _jobs = jobs;
        _plataforma = plataforma.Value;
        _logger = logger;
    }

    private static string ChaveCache(Guid usuarioId, Guid tenantId) => $"encerrar-conta:{usuarioId:N}:{tenantId:N}";

    // ---------- Tela "Meus dados" (administrador) ----------

    public async Task<SituacaoEncerramentoResponse> ObterSituacaoAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        var motivo = await MotivoParaNaoEncerrarAsync(tenantId, usuarioId, cancellationToken);
        var assinatura = await _db.Assinaturas.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        var notas = await _db.NotasFiscais.AsNoTracking().CountAsync(cancellationToken);
        return new SituacaoEncerramentoResponse(
            motivo is null, motivo, RegrasEncerramentoConta.DiasAteExpurgo,
            assinatura?.AsaasAssinaturaId is not null && assinatura.Status != StatusAssinatura.Cancelada,
            notas);
    }

    public async Task<SolicitacaoEncerramentoResponse> SolicitarCodigoAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        if (await MotivoParaNaoEncerrarAsync(tenantId, usuarioId, cancellationToken) is { } motivo)
            throw new RegraNegocioException(motivo);

        var email = await _db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
            throw new RegraNegocioException("Seu usuário não tem e-mail cadastrado para receber o código.");

        var conta = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.NomeFantasia ?? t.RazaoSocial).FirstAsync(cancellationToken);
        var codigo = CodigoExclusaoConta.Gerar();
        var corpo = $"""
            <p>Você pediu para <strong>encerrar</strong> a conta <strong>{WebUtility.HtmlEncode(conta)}</strong> no {DocumentosLegais.NomeServico}.</p>
            <p style="font-size:28px;letter-spacing:6px;font-weight:bold;margin:16px 0">{codigo}</p>
            <p>O código vale por {CodigoExclusaoConta.MinutosValidade} minutos e só pode ser usado uma vez.</p>
            <p>Ao encerrar, ninguém da conta entra mais, a assinatura é cancelada e, {RegrasEncerramentoConta.DiasAteExpurgo} dias depois, os dados são apagados (as notas fiscais ficam guardadas pelo prazo legal).</p>
            <p style="color:#64748b;font-size:12px">Se não foi você, ignore este e-mail e troque sua senha: sem o código, nada acontece.</p>
            """;

        // Direto pelo SMTP (sem fila): o código não fica gravado no EmailLog.
        var envio = await _emailSender.EnviarAsync(email, $"Código para encerrar a conta {conta}", corpo, cancellationToken);
        if (!envio.Sucesso)
        {
            _logger.LogError("Falha ao enviar o código de encerramento da conta {TenantId}: {Erro}", tenantId, envio.ErroDetalhe);
            throw new RegraNegocioException("Não foi possível enviar o e-mail com o código. Tente de novo em alguns minutos.");
        }

        _cache.Set(ChaveCache(usuarioId, tenantId),
            new CodigoPendente(CodigoExclusaoConta.Hash(codigo), DateTimeOffset.UtcNow.AddMinutes(CodigoExclusaoConta.MinutosValidade)),
            TimeSpan.FromMinutes(CodigoExclusaoConta.MinutosValidade));

        _logger.LogWarning("Usuário {UsuarioId} pediu o código para encerrar a conta {TenantId}.", usuarioId, tenantId);
        return new SolicitacaoEncerramentoResponse(CodigoExclusaoConta.MascararEmail(email), CodigoExclusaoConta.MinutosValidade);
    }

    public async Task<EncerramentoContaResponse> ConfirmarAsync(ConfirmarEncerramentoRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        var chave = ChaveCache(usuarioId, tenantId);
        if (!_cache.TryGetValue(chave, out CodigoPendente? pendente) || pendente is null || pendente.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(chave);
            throw new RegraNegocioException("O código expirou ou não foi pedido. Peça outro.");
        }

        if (!CodigoExclusaoConta.Confere(pendente.Hash, request.Codigo))
        {
            pendente.Tentativas++;
            if (pendente.Tentativas >= CodigoExclusaoConta.MaxTentativas)
            {
                _cache.Remove(chave);
                throw new RegraNegocioException("Código errado muitas vezes. Por segurança ele foi cancelado: peça um novo.");
            }
            throw new RegraNegocioException($"Código errado. Restam {CodigoExclusaoConta.MaxTentativas - pendente.Tentativas} tentativa(s).");
        }

        _cache.Remove(chave);
        if (await MotivoParaNaoEncerrarAsync(tenantId, usuarioId, cancellationToken) is { } motivo)
            throw new RegraNegocioException(motivo);

        return await EncerrarAsync(tenantId, OrigemEncerramento.Cliente, null, usuarioId, cancellationToken);
    }

    // ---------- Operador ----------

    public async Task<EncerramentoContaResponse> EncerrarPelaPlataformaAsync(Guid tenantId, string? motivo, Guid operadorId, CancellationToken cancellationToken)
    {
        if (tenantId == _currentTenant.TenantId || await TemOperadorAsync(tenantId, cancellationToken))
            throw new RegraNegocioException(RegrasEncerramentoConta.MotivoContaDaPlataforma);

        return await EncerrarAsync(tenantId, OrigemEncerramento.Plataforma, motivo, operadorId, cancellationToken);
    }

    // ---------- Encerrar (comum) ----------

    /// <summary>Também usado pelo encerramento automático (parte 2).</summary>
    internal async Task<EncerramentoContaResponse> EncerrarAsync(Guid tenantId, OrigemEncerramento origem, string? motivo, Guid? usuarioId, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Conta {tenantId} não encontrada.");
        if (tenant.EncerradaEm is not null)
            throw new RegraNegocioException(RegrasEncerramentoConta.MotivoJaEncerrada);

        // 1) Para de cobrar ANTES de fechar: se o Asaas recusar, nada muda aqui.
        var assinatura = await _db.Assinaturas.FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        var agora = DateTimeOffset.UtcNow;
        if (assinatura?.AsaasAssinaturaId is { } asaasId && assinatura.Status != StatusAssinatura.Cancelada)
        {
            if (!_gateway.Configurado)
                throw new RegraNegocioException("Não foi possível cancelar a assinatura (cobrança não configurada). Fale com o suporte.");
            await _gateway.CancelarAssinaturaAsync(asaasId, cancellationToken);
            assinatura.Status = StatusAssinatura.Cancelada;
            assinatura.CanceladaEm ??= agora;
            assinatura.AtrasadaDesde = null;
            assinatura.LimparFaturaPendente();
        }

        // 2) Fecha o acesso e marca o expurgo.
        var expurgo = RegrasEncerramentoConta.ExpurgoPrevisto(DataBrasilia.Hoje());
        tenant.Ativo = false;
        tenant.BloqueadoEm = agora;
        tenant.MotivoBloqueio = Limitar(RegrasEncerramentoConta.MotivoBloqueio(origem, motivo), 300);
        tenant.EncerradaEm = agora;
        tenant.EncerramentoOrigem = origem;
        tenant.ExpurgoPrevistoEm = expurgo;

        _db.TenantIdOverrideDeSistema = tenantId;
        try
        {
            _auditLogWriter.Registrar("ContaEncerrada", "Tenant", tenantId, new
            {
                Origem = origem.ToString(),
                Motivo = motivo,
                UsuarioId = usuarioId,
                ExpurgoPrevistoEm = expurgo,
                AssinaturaCancelada = assinatura?.Status == StatusAssinatura.Cancelada
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }

        // 3) Derruba as sessões (mesmo mecanismo da suspensão).
        var novoStamp = Guid.NewGuid().ToString("N").ToUpperInvariant();
        await _db.Users.Where(u => u.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, novoStamp), cancellationToken);

        _logger.LogWarning("Conta {TenantId} ({RazaoSocial}) ENCERRADA ({Origem}) por {UsuarioId}; expurgo em {Expurgo:dd/MM/yyyy}.",
            tenantId, tenant.RazaoSocial, origem, usuarioId, expurgo);

        await AvisarAdministradoresAsync(tenant, CancellationToken.None);
        return new EncerramentoContaResponse(agora, expurgo);
    }

    private async Task AvisarAdministradoresAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        try
        {
            var agora = DateTimeOffset.UtcNow;
            var destinatarios = await (
                from u in _db.Users.AsNoTracking()
                join g in _db.Grupos.IgnoreQueryFilters().AsNoTracking() on u.GrupoId equals g.Id
                where u.TenantId == tenant.Id && g.TenantId == tenant.Id && g.EhAdministrador && u.Email != null
                select new { u.Id, Email = u.Email! }).ToListAsync(cancellationToken);
            if (destinatarios.Count == 0)
                return;

            var quando = tenant.EncerradaEm!.Value.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy");
            var expurgo = tenant.ExpurgoPrevistoEm!.Value.ToString("dd/MM/yyyy");
            var corpo = $"""
                <p>Olá,</p>
                <p>A conta <strong>{WebUtility.HtmlEncode(tenant.NomeFantasia ?? tenant.RazaoSocial)}</strong> no {DocumentosLegais.NomeServico} foi <strong>encerrada em {quando}</strong>. O acesso foi fechado e a assinatura, se havia, foi cancelada.</p>
                <p>Em <strong>{expurgo}</strong> os dados da conta serão apagados. As notas fiscais emitidas ficam guardadas pelo prazo exigido por lei (5 anos), sem acesso pela plataforma.</p>
                <p>Até {expurgo}, para reabrir a conta ou receber uma cópia dos seus dados, escreva para {DocumentosLegais.EmailPrivacidade}.</p>
                <p style="color:#64748b;font-size:12px">Se você não pediu este encerramento, fale com {DocumentosLegais.EmailPrivacidade} o quanto antes.</p>
                """;

            _db.TenantIdOverrideDeSistema = tenant.Id;
            try
            {
                foreach (var d in destinatarios.DistinctBy(d => d.Email.Trim().ToLowerInvariant()))
                    await _emailQueue.EnfileirarAsync(new EmailParaEnfileirar(
                        TipoEmailCatalogo.EncerramentoConta, d.Id, d.Email.Trim(), "Sua conta foi encerrada", corpo,
                        ChaveDeduplicacao: $"encerramento:{tenant.Id:N}:{tenant.EncerradaEm:yyyyMMddHHmmss}"), cancellationToken);
            }
            finally
            {
                _db.TenantIdOverrideDeSistema = null;
            }
        }
        catch (Exception ex)
        {
            // A conta já está encerrada; o e-mail é aviso.
            _logger.LogWarning(ex, "Não foi possível avisar o encerramento da conta {TenantId} por e-mail.", tenant.Id);
        }
    }

    // ---------- Expurgo (job diário) ----------

    public async Task<ResultadoExpurgoContas> ExecutarAsync(DateOnly hoje, CancellationToken cancellationToken)
    {
        var candidatas = await _db.Tenants.AsNoTracking()
            .Where(t => t.EncerradaEm != null && t.DadosExpurgadosEm == null && t.ExpurgoPrevistoEm != null && t.ExpurgoPrevistoEm <= hoje)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        int excluidas = 0, anonimizadas = 0, comErro = 0;
        foreach (var tenantId in candidatas)
        {
            try
            {
                // Conta de operador nunca é apagada, mesmo encerrada por engano no banco.
                if (await TemOperadorAsync(tenantId, cancellationToken))
                {
                    _logger.LogError("Expurgo: a conta {TenantId} tem operador da plataforma e não foi apagada.", tenantId);
                    comErro++;
                    continue;
                }

                var temNotas = await _db.NotasFiscais.IgnoreQueryFilters().AnyAsync(n => n.TenantId == tenantId, cancellationToken);
                if (temNotas)
                {
                    await AnonimizarAsync(tenantId, cancellationToken);
                    anonimizadas++;
                }
                else
                {
                    await _exclusao.ExcluirDefinitivamenteAsync(tenantId, operadorId: null, cancellationToken);
                    excluidas++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                comErro++;
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Expurgo da conta encerrada {TenantId} falhou; tenta de novo amanhã.", tenantId);
            }
        }

        if (excluidas + anonimizadas + comErro > 0)
            _logger.LogWarning("Expurgo das contas encerradas: {Excluidas} excluída(s), {Anonimizadas} com dados apagados (notas guardadas), {ComErro} com erro.",
                excluidas, anonimizadas, comErro);

        return new ResultadoExpurgoContas(excluidas, anonimizadas, comErro);
    }

    /// <summary>
    /// Conta com notas: apaga tudo o que não é necessário para os documentos
    /// fiscais. Fica: a conta (titular), empresas (prestador, sem certificado),
    /// tomadores que aparecem nas notas (sem e-mail/telefone), notas e eventos,
    /// assinatura e eventos de cobrança, prêmios e aceites dos termos.
    /// Numa transação; arquivos e jobs depois.
    /// </summary>
    internal async Task AnonimizarAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var empresas = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId).Select(e => e.Id).ToListAsync(cancellationToken);

        var estrategia = _db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _db.Database.BeginTransactionAsync(cancellationToken);

            // As notas deixam de apontar para contrato/serviço (os dados da nota ficam nela).
            await _db.NotasFiscais.IgnoreQueryFilters().Where(n => n.TenantId == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ContratoId, (Guid?)null).SetProperty(n => n.ServicoId, (Guid?)null), cancellationToken);

            await _db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ContratoDocumentos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.DocumentosEmpresa.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.SaidasCopiaEmail.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ConfirmacoesRecebimentoNota.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.DocumentosGerenciadosEmpresa.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ConfiguracoesAutomacaoNotaMensal.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ContadoresDps.Where(x => empresas.Contains(x.EmpresaId)).ExecuteDeleteAsync(cancellationToken);

            // Tomadores: sem nota, saem; com nota, ficam sem contato.
            await _db.Clientes.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && !_db.NotasFiscais.IgnoreQueryFilters().Any(n => n.ClienteId == c.Id))
                .ExecuteDeleteAsync(cancellationToken);
            await _db.Clientes.IgnoreQueryFilters().Where(c => c.TenantId == tenantId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Email, (string?)null)
                    .SetProperty(c => c.EmailsCopia, (string?)null)
                    .SetProperty(c => c.Telefone, (string?)null)
                    .SetProperty(c => c.EnviarNfsePorEmail, false)
                    .SetProperty(c => c.Ativo, false), cancellationToken);

            await _db.Empresas.IgnoreQueryFilters().Where(e => e.TenantId == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Ativo, false), cancellationToken);

            await _db.ConvitesIndicacao.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ExportacoesDados.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.LogosConta.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ModelosEmailNota.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);

            // Usuários: papéis, claims, logins e tokens saem junto (FK CASCADE no banco).
            await _db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.GrupoTelas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);

            var agora = DateTimeOffset.UtcNow;
            await _db.Tenants.Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.DadosExpurgadosEm, agora)
                    .SetProperty(t => t.PaletaCores, (string?)null)
                    .SetProperty(t => t.WhatsApp, (string?)null)
                    .SetProperty(t => t.WhatsAppAutorizadoEm, (DateTimeOffset?)null)
                    .SetProperty(t => t.LembreteWhatsAppAdiadoAte, (DateTimeOffset?)null), cancellationToken);

            // Único registro que sobra no Histórico: quando e o que foi apagado.
            _db.TenantIdOverrideDeSistema = tenantId;
            try
            {
                _auditLogWriter.Registrar("DadosDaContaApagados", "Tenant", tenantId, new { Motivo = "Expurgo após o encerramento", NotasGuardadas = true });
                await _db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _db.TenantIdOverrideDeSistema = null;
            }

            await transacao.CommitAsync(cancellationToken);
        });

        foreach (var empresaId in empresas)
        {
            Tentar(() => _certificados.Excluir(empresaId), "certificado", empresaId);
            Tentar(() => _jobs.RemoveIfExists(AutomacaoNotaMensalJobHelper.ObterJobId(empresaId)), "job da Nota Mensal", empresaId);
        }
        Tentar(() => _documentos.ExcluirPastaConta(tenantId), "pasta de documentos", tenantId);

        _logger.LogWarning("Conta encerrada {TenantId}: dados apagados, notas fiscais guardadas.", tenantId);
    }

    // ---------- Apoio ----------

    private async Task<string?> MotivoParaNaoEncerrarAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var grupoId = await _db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
            .Select(u => u.GrupoId)
            .FirstOrDefaultAsync(cancellationToken);
        var administrador = grupoId is { } id && await _db.Grupos.AsNoTracking()
            .AnyAsync(g => g.Id == id && g.TenantId == tenantId && g.EhAdministrador, cancellationToken);
        var encerrada = await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && t.EncerradaEm != null, cancellationToken);
        return RegrasEncerramentoConta.MotivoParaNaoEncerrar(administrador, encerrada, await TemOperadorAsync(tenantId, cancellationToken));
    }

    private Task<bool> TemOperadorAsync(Guid tenantId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.TenantId == tenantId && _plataforma.UsuariosOperadoresIds.Contains(u.Id), cancellationToken);

    private (Guid TenantId, Guid UsuarioId) Identificar() =>
        _currentTenant.TenantId is { } t && _currentUser.UserId is { } u
            ? (t, u)
            : throw new InvalidOperationException("Conta ou usuário não identificado.");

    private void Tentar(Action acao, string oQue, Guid id)
    {
        try
        {
            acao();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Expurgo: falhou ao apagar {OQue} ({Id}). Apague manualmente.", oQue, id);
        }
    }

    private static string Limitar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
