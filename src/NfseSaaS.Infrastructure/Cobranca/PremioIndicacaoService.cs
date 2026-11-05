using System.Globalization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Prêmio do "Indique e ganhe" (1 mês grátis), aplicado no Asaas:
/// 1. Se já existe a próxima fatura em aberto da assinatura (o Asaas gera
///    com até 40 dias de antecedência), ela é REMOVIDA: o cliente não paga
///    aquele mês e a assinatura segue normalmente.
/// 2. Se ainda não existe, o próximo vencimento (nextDueDate) é adiado em
///    1 mês.
/// Vários prêmios da mesma conta somam meses (cada um pula o mês seguinte).
///
/// Segurança contra mês em dobro: o prêmio é gravado como "Aplicando"
/// ANTES de chamar o Asaas. Se o processo cair no meio, ele vira Erro na
/// primeira passada depois de TempoMaximoAplicando e só o operador o reabre
/// (depois de conferir no Asaas).
/// Recusa explícita do Asaas (RegraNegocioException) não mudou nada lá:
/// volta a Pendente e tenta de novo, até RegrasPremioIndicacao.MaxTentativas.
/// </summary>
public sealed class PremioIndicacaoService : IAplicarPremiosIndicacaoUseCase, IListarPremiosIndicacaoPlataformaUseCase, IReabrirPremioIndicacaoUseCase
{
    /// <summary>
    /// "Aplicando" parado há mais que isto é queda no meio. Acima do limite de
    /// execução do job (DisableConcurrentExecution de 600 s): um prêmio sendo
    /// aplicado AGORA por outra execução nunca é tomado por interrompido.
    /// </summary>
    public static readonly TimeSpan TempoMaximoAplicando = TimeSpan.FromMinutes(15);

    private const string MotivoInterrompido =
        "A aplicação foi interrompida no meio. Confira no Asaas se a fatura já foi dispensada antes de tentar de novo.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly IGatewayCobranca _gateway;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IBackgroundJobClient _jobs;
    private readonly CobrancaOptions _options;
    private readonly ILogger<PremioIndicacaoService> _logger;

    public PremioIndicacaoService(
        AppDbContext db,
        IGatewayCobranca gateway,
        IAuditLogWriter auditLogWriter,
        IBackgroundJobClient jobs,
        IOptions<CobrancaOptions> options,
        ILogger<PremioIndicacaoService> logger)
    {
        _db = db;
        _gateway = gateway;
        _auditLogWriter = auditLogWriter;
        _jobs = jobs;
        _options = options.Value;
        _logger = logger;
    }

    // ---------- Aplicar (job) ----------

    async Task<ResultadoAplicacaoPremios> IAplicarPremiosIndicacaoUseCase.ExecutarAsync(CancellationToken cancellationToken)
    {
        // Sobrou "Aplicando" de uma passada que caiu: não repete sozinho.
        var limiteInterrompido = DateTimeOffset.UtcNow - TempoMaximoAplicando;
        var interrompidos = await _db.PremiosIndicacao
            .Where(p => p.Status == StatusPremioIndicacao.Aplicando && (p.UpdatedAt ?? p.CreatedAt) < limiteInterrompido)
            .ToListAsync(cancellationToken);
        foreach (var premio in interrompidos)
        {
            premio.Status = StatusPremioIndicacao.Erro;
            premio.Detalhe = MotivoInterrompido;
            premio.UpdatedAt = DateTimeOffset.UtcNow;
            _logger.LogError("Prêmio de indicação {PremioId} da conta {TenantId} ficou interrompido: conferir no Asaas.", premio.Id, premio.TenantId);
        }
        if (interrompidos.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        if (!_gateway.Configurado)
            return new ResultadoAplicacaoPremios(0, 0, interrompidos.Count);

        var pendentes = await _db.PremiosIndicacao
            .Where(p => p.Status == StatusPremioIndicacao.Pendente)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        int aplicados = 0, aguardando = 0, comErro = interrompidos.Count;
        foreach (var premio in pendentes)
        {
            switch (await AplicarAsync(premio, cancellationToken))
            {
                case StatusPremioIndicacao.Aplicado: aplicados++; break;
                case StatusPremioIndicacao.Erro: comErro++; break;
                default: aguardando++; break;
            }
        }

        return new ResultadoAplicacaoPremios(aplicados, aguardando, comErro);
    }

    private async Task<StatusPremioIndicacao> AplicarAsync(PremioIndicacao premio, CancellationToken cancellationToken)
    {
        var assinatura = await _db.Assinaturas.FirstOrDefaultAsync(a => a.TenantId == premio.TenantId, cancellationToken);
        var contaAtiva = await _db.Tenants.AsNoTracking().Where(t => t.Id == premio.TenantId).Select(t => t.Ativo).FirstOrDefaultAsync(cancellationToken);

        var temAssinaturaNoGateway = assinatura?.AsaasAssinaturaId is not null;
        var motivo = RegrasPremioIndicacao.MotivoEspera(
            assinatura?.Status, temAssinaturaNoGateway, _options.ContasIsentas.Contains(premio.TenantId), contaAtiva);

        if (motivo is not null)
        {
            if (premio.Detalhe != motivo)
            {
                premio.Detalhe = motivo;
                premio.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return StatusPremioIndicacao.Pendente;
        }

        var assinaturaId = assinatura!.AsaasAssinaturaId!;

        premio.Status = StatusPremioIndicacao.Aplicando;
        premio.Tentativas++;
        premio.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            // A conta indicada acabou de pagar a fatura de origem: nunca é ela a "próxima".
            var ignorar = premio.Papel == PapelPremioIndicacao.ContaIndicada ? premio.AsaasPagamentoOrigemId : null;
            var fatura = await _gateway.ObterProximaFaturaEmAbertoAsync(assinaturaId, ignorar, cancellationToken);

            if (fatura is not null)
            {
                await _gateway.RemoverFaturaAsync(fatura.Id, cancellationToken);
                premio.AsaasPagamentoDispensadoId = fatura.Id;
                premio.VencimentoDispensado = fatura.Vencimento;
                premio.Detalhe = $"Fatura de {fatura.Vencimento.ToString("dd/MM/yyyy", PtBr)} dispensada (1 mês grátis).";
                if (fatura.Url is not null && assinatura.UrlFaturaPendente == fatura.Url)
                    assinatura.UrlFaturaPendente = null;
            }
            else
            {
                var proximo = await _gateway.ObterProximoVencimentoAsync(assinaturaId, cancellationToken)
                    ?? throw new RegraNegocioException("O Asaas não informou o próximo vencimento da assinatura.");
                var novo = proximo.AddMonths(1);
                await _gateway.AlterarProximoVencimentoAsync(assinaturaId, novo, cancellationToken);
                premio.VencimentoDispensado = proximo;
                premio.Detalhe = $"Próximo vencimento adiado de {proximo.ToString("dd/MM/yyyy", PtBr)} para {novo.ToString("dd/MM/yyyy", PtBr)} (1 mês grátis).";
            }

            premio.Status = StatusPremioIndicacao.Aplicado;
            premio.AplicadoEm = DateTimeOffset.UtcNow;
            premio.UpdatedAt = premio.AplicadoEm;

            // Fica no Histórico da conta premiada.
            _db.TenantIdOverrideDeSistema = premio.TenantId;
            try
            {
                _auditLogWriter.Registrar("PremioIndicacaoAplicado", "PremioIndicacao", premio.Id, new
                {
                    Papel = premio.Papel.ToString(),
                    premio.VencimentoDispensado,
                    premio.AsaasPagamentoDispensadoId
                });
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            finally
            {
                _db.TenantIdOverrideDeSistema = null;
            }

            _logger.LogInformation("Prêmio de indicação {PremioId} aplicado na conta {TenantId}: {Detalhe}", premio.Id, premio.TenantId, premio.Detalhe);
            return StatusPremioIndicacao.Aplicado;
        }
        catch (RegraNegocioException ex)
        {
            // O Asaas recusou: nada mudou lá. Tenta de novo na próxima passada (até o limite).
            premio.Status = RegrasPremioIndicacao.StatusAposRecusa(premio.Tentativas);
            premio.Detalhe = Limitar(ex.Message, 300);
            premio.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Prêmio de indicação {PremioId} recusado pelo Asaas (tentativa {Tentativa}): {Mensagem}", premio.Id, premio.Tentativas, ex.Message);
            return premio.Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sem resposta clara (queda, tempo esgotado): pode ter sido aplicado. Operador confere.
            premio.Status = StatusPremioIndicacao.Erro;
            premio.Detalhe = "Sem resposta do Asaas. Confira no painel do Asaas se a fatura foi dispensada antes de tentar de novo.";
            premio.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Falha ao aplicar o prêmio de indicação {PremioId} da conta {TenantId}.", premio.Id, premio.TenantId);
            return StatusPremioIndicacao.Erro;
        }
    }

    // ---------- Operador ----------

    async Task<IReadOnlyList<PremioIndicacaoPlataformaResponse>> IListarPremiosIndicacaoPlataformaUseCase.ExecutarAsync(CancellationToken cancellationToken)
    {
        var premios = await _db.PremiosIndicacao.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(300)
            .ToListAsync(cancellationToken);

        var ids = premios.SelectMany(p => new[] { p.TenantId, p.IndicadoTenantId }).Distinct().ToList();
        var nomes = await _db.Tenants.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.NomeFantasia ?? t.RazaoSocial, cancellationToken);

        return premios.Select(p => new PremioIndicacaoPlataformaResponse(
            p.Id, p.TenantId, nomes.GetValueOrDefault(p.TenantId, "?"), nomes.GetValueOrDefault(p.IndicadoTenantId, "?"),
            p.Papel, p.Status, p.CreatedAt, p.AplicadoEm, p.VencimentoDispensado, p.Tentativas, p.Detalhe)).ToList();
    }

    async Task IReabrirPremioIndicacaoUseCase.ExecutarAsync(Guid premioId, Guid? operadorId, CancellationToken cancellationToken)
    {
        var premio = await _db.PremiosIndicacao.FirstOrDefaultAsync(p => p.Id == premioId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Prêmio {premioId} não encontrado.");

        if (premio.Status != StatusPremioIndicacao.Erro)
            throw new RegraNegocioException("Só um prêmio com erro pode ser tentado de novo.");

        premio.Status = StatusPremioIndicacao.Pendente;
        premio.Tentativas = 0;
        premio.Detalhe = "Reaberto pelo operador.";
        premio.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Prêmio de indicação {PremioId} reaberto pelo operador {OperadorId}.", premioId, operadorId);
        _jobs.Enqueue<IAplicarPremiosIndicacaoJob>(job => job.ExecutarAsync());
    }

    private static string Limitar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
