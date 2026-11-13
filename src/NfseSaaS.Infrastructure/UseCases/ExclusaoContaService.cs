using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Exclusão DEFINITIVA de uma conta (Tenant) pelo operador da plataforma,
/// em dois passos (pedir código → confirmar com o código).
///
/// Regras (conferidas nos dois passos):
/// - nunca registrou NENHUMA nota (qualquer status ou ambiente): nota é
///   documento fiscal e não pode sumir;
/// - sem assinatura ativa no Asaas (cancelar antes, para não cobrar quem
///   não existe mais);
/// - não é a conta do próprio operador nem tem operador da plataforma.
///
/// O código vai DIRETO pelo SMTP para o e-mail do operador logado, sem
/// passar pela fila (a fila grava o corpo no EmailLog). Em memória fica só
/// o hash, por CodigoExclusaoConta.MinutosValidade minutos e no máximo
/// CodigoExclusaoConta.MaxTentativas tentativas, ligado ao par
/// (operador, conta). Reiniciar o sistema invalida o código (pede outro).
///
/// O que é apagado: tudo da conta no banco (numa transação), os arquivos de
/// certificado e de documentos dos contratos e os jobs da Nota Mensal. Fica
/// só um registro no Histórico da conta do OPERADOR, sem nome nem CNPJ.
/// Os backups antigos guardam os dados até girarem (ver backup-restauracao).
/// </summary>
public sealed class ExclusaoContaService : ISolicitarExclusaoContaUseCase, IConfirmarExclusaoContaUseCase
{
    private sealed record CodigoPendente(byte[] Hash, DateTimeOffset ExpiraEm)
    {
        public int Tentativas { get; set; }
    }

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IMemoryCache _cache;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly CertificateFileStore _certificados;
    private readonly ContratoDocumentoFileStore _documentos;
    private readonly IRecurringJobManager _jobs;
    private readonly PlataformaOptions _plataforma;
    private readonly ILogger<ExclusaoContaService> _logger;

    public ExclusaoContaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        IMemoryCache cache,
        IEmailSender emailSender,
        IAuditLogWriter auditLogWriter,
        CertificateFileStore certificados,
        ContratoDocumentoFileStore documentos,
        IRecurringJobManager jobs,
        IOptions<PlataformaOptions> plataforma,
        ILogger<ExclusaoContaService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _cache = cache;
        _emailSender = emailSender;
        _auditLogWriter = auditLogWriter;
        _certificados = certificados;
        _documentos = documentos;
        _jobs = jobs;
        _plataforma = plataforma.Value;
        _logger = logger;
    }

    private static string ChaveCache(Guid operadorId, Guid tenantId) => $"exclusao-conta:{operadorId:N}:{tenantId:N}";

    // ---------- Passo 1: pedir o código ----------

    async Task<SolicitacaoExclusaoContaResponse> ISolicitarExclusaoContaUseCase.ExecutarAsync(Guid tenantId, Guid operadorId, CancellationToken cancellationToken)
    {
        var nome = await ValidarPodeExcluirAsync(tenantId, cancellationToken);

        var emailOperador = await _db.Users.AsNoTracking()
            .Where(u => u.Id == operadorId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(emailOperador))
            throw new RegraNegocioException("Seu usuário não tem e-mail cadastrado para receber o código.");

        var codigo = CodigoExclusaoConta.Gerar();
        var assunto = $"Código para excluir a conta {nome}";
        var corpo = $"""
            <p>Você pediu para <strong>excluir definitivamente</strong> a conta <strong>{WebUtility.HtmlEncode(nome)}</strong> no micoempresa.</p>
            <p style="font-size:28px;letter-spacing:6px;font-weight:bold;margin:16px 0">{codigo}</p>
            <p>O código vale por {CodigoExclusaoConta.MinutosValidade} minutos e só pode ser usado uma vez.</p>
            <p style="color:#64748b;font-size:12px">Se não foi você, ignore este e-mail e troque sua senha: sem o código, nada é excluído.</p>
            """;

        // Direto pelo SMTP (sem fila): o código não fica gravado no EmailLog.
        var envio = await _emailSender.EnviarAsync(emailOperador, assunto, corpo, cancellationToken);
        if (!envio.Sucesso)
        {
            _logger.LogError("Falha ao enviar o código de exclusão da conta {TenantId}: {Erro}", tenantId, envio.ErroDetalhe);
            throw new RegraNegocioException("Não foi possível enviar o e-mail com o código. Confira a configuração de e-mail e tente de novo.");
        }

        // Pedir de novo substitui o código anterior.
        _cache.Set(ChaveCache(operadorId, tenantId),
            new CodigoPendente(CodigoExclusaoConta.Hash(codigo), DateTimeOffset.UtcNow.AddMinutes(CodigoExclusaoConta.MinutosValidade)),
            TimeSpan.FromMinutes(CodigoExclusaoConta.MinutosValidade));

        _logger.LogWarning("Operador {OperadorId} pediu o código para excluir a conta {TenantId}.", operadorId, tenantId);

        var contagem = await ContarAsync(tenantId, cancellationToken);
        return new SolicitacaoExclusaoContaResponse(
            CodigoExclusaoConta.MascararEmail(emailOperador), CodigoExclusaoConta.MinutosValidade,
            contagem.Empresas, contagem.Clientes, contagem.Contratos, contagem.Servicos, contagem.Usuarios);
    }

    // ---------- Passo 2: confirmar com o código ----------

    async Task IConfirmarExclusaoContaUseCase.ExecutarAsync(Guid tenantId, Guid operadorId, string codigo, CancellationToken cancellationToken)
    {
        var chave = ChaveCache(operadorId, tenantId);
        if (!_cache.TryGetValue(chave, out CodigoPendente? pendente) || pendente is null || pendente.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(chave);
            throw new RegraNegocioException("O código expirou ou não foi pedido. Clique em Excluir de novo para receber outro.");
        }

        if (!CodigoExclusaoConta.Confere(pendente.Hash, codigo))
        {
            pendente.Tentativas++;
            if (pendente.Tentativas >= CodigoExclusaoConta.MaxTentativas)
            {
                _cache.Remove(chave);
                _logger.LogWarning("Código de exclusão da conta {TenantId} bloqueado após {Tentativas} tentativas erradas (operador {OperadorId}).",
                    tenantId, pendente.Tentativas, operadorId);
                throw new RegraNegocioException("Código errado muitas vezes. Por segurança ele foi cancelado: peça um novo.");
            }
            throw new RegraNegocioException($"Código errado. Restam {CodigoExclusaoConta.MaxTentativas - pendente.Tentativas} tentativa(s).");
        }

        // Uso único, mesmo que algo falhe daqui em diante.
        _cache.Remove(chave);

        await ValidarPodeExcluirAsync(tenantId, cancellationToken);
        await ExcluirDefinitivamenteAsync(tenantId, operadorId, cancellationToken);
    }

    /// <summary>
    /// Apaga a conta inteira (banco, certificados, documentos, jobs). Quem
    /// chama já conferiu as regras: o operador (código) ou o expurgo das
    /// contas encerradas sem notas (operadorId nulo: sem Histórico, só log).
    /// </summary>
    internal async Task ExcluirDefinitivamenteAsync(Guid tenantId, Guid? operadorId, CancellationToken cancellationToken)
    {
        var contagem = await ContarAsync(tenantId, cancellationToken);

        var empresas = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        await ApagarNoBancoAsync(tenantId, empresas, operadorId, contagem, cancellationToken);

        // Depois do banco: arquivos e jobs. Falha aqui não volta a conta;
        // vai para o log (o operador apaga à mão o que sobrar).
        foreach (var empresaId in empresas)
        {
            Tentar(() => _certificados.Excluir(empresaId), "certificado", empresaId);
            Tentar(() => _jobs.RemoveIfExists(AutomacaoNotaMensalJobHelper.ObterJobId(empresaId)), "job da Nota Mensal", empresaId);
        }
        Tentar(() => _documentos.ExcluirPastaConta(tenantId), "pasta de documentos", tenantId);

        _logger.LogWarning("Conta {TenantId} excluída {Por}: {Empresas} empresa(s), {Usuarios} usuário(s).",
            tenantId, operadorId is null ? "pelo expurgo das contas encerradas" : $"pelo operador {operadorId}", contagem.Empresas, contagem.Usuarios);
    }

    // ---------- Regras ----------

    /// <summary>Lança RegraNegocioException quando a conta não pode ser excluída; devolve o nome dela.</summary>
    private async Task<string> ValidarPodeExcluirAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Conta {tenantId} não encontrada.");

        if (tenantId == _currentTenant.TenantId)
            throw new RegraNegocioException("Você não pode excluir a própria conta da plataforma.");

        if (await _db.Users.AnyAsync(u => u.TenantId == tenantId && _plataforma.UsuariosOperadoresIds.Contains(u.Id), cancellationToken))
            throw new RegraNegocioException("Esta conta tem um operador da plataforma e não pode ser excluída.");

        var notas = await _db.NotasFiscais.IgnoreQueryFilters().CountAsync(n => n.TenantId == tenantId, cancellationToken);
        if (notas > 0)
            throw new RegraNegocioException(
                $"Esta conta já registrou {notas} nota(s) (homologação ou produção) e não pode ser excluída: nota é documento fiscal. Use Suspender.");

        var assinatura = await _db.Assinaturas.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        if (assinatura?.AsaasAssinaturaId is not null && assinatura.Status != StatusAssinatura.Cancelada)
            throw new RegraNegocioException(
                "Esta conta tem assinatura ativa no Asaas. Cancele a assinatura antes de excluir (para não gerar cobrança de quem não existe mais).");

        return tenant.NomeFantasia ?? tenant.RazaoSocial;
    }

    private sealed record Contagem(int Empresas, int Clientes, int Contratos, int Servicos, int Usuarios);

    private async Task<Contagem> ContarAsync(Guid tenantId, CancellationToken cancellationToken) => new(
        await _db.Empresas.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenantId, cancellationToken),
        await _db.Clientes.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantId, cancellationToken),
        await _db.Contratos.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantId, cancellationToken),
        await _db.Servicos.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, cancellationToken),
        await _db.Users.CountAsync(u => u.TenantId == tenantId, cancellationToken));

    /// <summary>
    /// Tudo numa transação, dos filhos para os pais (as FKs são RESTRICT).
    /// Nota fiscal não entra: a regra garante que não existe nenhuma, e se
    /// uma aparecer no meio do caminho a FK de Empresa barra e nada é apagado.
    /// </summary>
    private async Task ApagarNoBancoAsync(Guid tenantId, List<Guid> empresas, Guid? operadorId, Contagem contagem, CancellationToken cancellationToken)
    {
        var estrategia = _db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _db.Database.BeginTransactionAsync(cancellationToken);

            await _db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ContratoDocumentos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.DocumentosEmpresa.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.DocumentosGerenciadosEmpresa.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ConfiguracoesAutomacaoNotaMensal.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ContadoresDps.Where(x => empresas.Contains(x.EmpresaId)).ExecuteDeleteAsync(cancellationToken);
            await _db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);

            await _db.AceitesTermos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ConvitesIndicacao.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            // Os arquivos ZIP saem na limpeza diária (pasta de conta sem pedido vivo).
            await _db.ExportacoesDados.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.LogosConta.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ModelosEmailNota.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);

            // Usuários: papéis, claims, logins e tokens saem junto (FK CASCADE no banco).
            await _db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.GrupoTelas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);

            await _db.PremiosIndicacao.Where(x => x.TenantId == tenantId || x.IndicadoTenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.EventosCobranca.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.ReajustesAssinatura.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            await _db.Assinaturas.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
            // testes_gratis_cnpj fica de propósito: guarda só o CNPJ que já usou o
            // teste grátis (senão bastaria excluir a conta e criar outra).

            // Contas que vieram pelo link desta continuam existindo, só sem "indicada por".
            await _db.Tenants.Where(x => x.IndicadoPorTenantId == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IndicadoPorTenantId, (Guid?)null), cancellationToken);
            await _db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync(cancellationToken);

            // Registro no Histórico da conta do OPERADOR, sem dado da conta apagada.
            // No expurgo automático não há operador (nem conta atual): fica só o log.
            if (operadorId is not null)
            {
                _auditLogWriter.Registrar("ContaExcluidaPelaPlataforma", "Tenant", tenantId, new
                {
                    OperadorId = operadorId,
                    contagem.Empresas,
                    contagem.Clientes,
                    contagem.Contratos,
                    contagem.Servicos,
                    contagem.Usuarios
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            await transacao.CommitAsync(cancellationToken);
        });
    }

    private void Tentar(Action acao, string oQue, Guid id)
    {
        try
        {
            acao();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conta excluída, mas falhou ao apagar {OQue} ({Id}). Apague manualmente.", oQue, id);
        }
    }
}
