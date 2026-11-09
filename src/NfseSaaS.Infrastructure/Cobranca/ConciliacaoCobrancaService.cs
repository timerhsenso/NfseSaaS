using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Conciliação de uma conta com o Asaas (L5): lê o estado real da
/// assinatura e das faturas, decide com <see cref="RegrasConciliacao"/> e
/// grava só o que mudou, com Histórico na conta ("ConciliarCobranca").
/// Conta sem assinatura no Asaas, ou que o Asaas não conhece, não muda.
///
/// Prêmio de indicação: quando a conciliação descobre o PRIMEIRO pagamento
/// (o webhook se perdeu), registra os prêmios do "Indique e ganhe" na mesma
/// gravação, como o webhook faria. Depois de virar Ativa (prêmio novo ou
/// guardado de quem indicou), dispara a aplicação por job.
/// </summary>
public sealed class ConciliacaoCobrancaService : IConciliarCobrancaUseCase
{
    public const string OperacaoHistorico = "ConciliarCobranca";

    private readonly AppDbContext _db;
    private readonly IGatewayCobranca _gateway;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IBackgroundJobClient _jobs;
    private readonly CobrancaOptions _cobranca;
    private readonly ILogger<ConciliacaoCobrancaService> _logger;

    public ConciliacaoCobrancaService(
        AppDbContext db,
        IGatewayCobranca gateway,
        IAuditLogWriter auditLogWriter,
        IBackgroundJobClient jobs,
        IOptions<CobrancaOptions> cobranca,
        ILogger<ConciliacaoCobrancaService> logger)
    {
        _db = db;
        _gateway = gateway;
        _auditLogWriter = auditLogWriter;
        _jobs = jobs;
        _cobranca = cobranca.Value;
        _logger = logger;
    }

    public async Task<ConciliacaoContaResponse> ConciliarAsync(Guid tenantId, Guid? operadorId, CancellationToken cancellationToken)
    {
        if (!_gateway.Configurado)
            throw new RegraNegocioException("A cobrança ainda não está configurada (chave do Asaas ausente): não há o que conferir.");

        var assinatura = await _db.Assinaturas.FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        if (assinatura?.AsaasAssinaturaId is not { } asaasId)
            return new ConciliacaoContaResponse(false, "Esta conta não tem assinatura no Asaas: nada a conferir.", []);

        var noGateway = await _gateway.ObterParaConciliacaoAsync(asaasId, cancellationToken);
        if (noGateway is null)
        {
            _logger.LogWarning("Conciliação: a assinatura {AsaasId} da conta {TenantId} não existe no Asaas (chave ou ambiente trocado?). Nada alterado.",
                asaasId, tenantId);
            return new ConciliacaoContaResponse(false,
                "O Asaas não encontrou a assinatura desta conta (chave ou ambiente diferente?). Nada foi alterado.", []);
        }

        // Mesmo limite da coluna (300): sem isso, link longo "mudaria" todo dia.
        var faturas = noGateway.Faturas.Select(f => f with { Url = Limitar(f.Url, 300) }).ToList();
        var local = new EstadoLocalConciliacao(
            assinatura.Status, assinatura.AtrasadaDesde, assinatura.UltimoPagamentoEm,
            assinatura.UrlFaturaPendente, assinatura.VencimentoFaturaPendente, assinatura.ValorFaturaPendente);
        var resultado = RegrasConciliacao.Avaliar(local, noGateway with { Faturas = faturas }, DataBrasilia.Hoje());

        if (!resultado.Mudou)
            return new ConciliacaoContaResponse(false, "Já estava igual ao Asaas: nada mudou.", []);

        var antes = new { Status = local.Status.ToString(), local.AtrasadaDesde, local.UltimoPagamentoEm, local.UrlFaturaPendente, local.VencimentoFaturaPendente };

        assinatura.Status = resultado.Status;
        assinatura.AtrasadaDesde = resultado.AtrasadaDesde;
        if (resultado.Status == StatusAssinatura.Cancelada)
            assinatura.CanceladaEm ??= DateTimeOffset.UtcNow;
        if (resultado.PrimeiroPagamentoEm is { } pagoEm)
            // Meio-dia de Brasília, em UTC (o Npgsql só grava timestamptz com offset 0).
            assinatura.UltimoPagamentoEm = new DateTimeOffset(pagoEm.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);

        var mudancas = resultado.Mudancas.ToList();
        var premioRegistrado = resultado.PrimeiroPagamentoEm is not null
            && await RegistroPremiosIndicacao.RegistrarAsync(_db, tenantId, resultado.PrimeiroPagamentoId, cancellationToken);
        if (premioRegistrado)
            mudancas.Add("prêmio de indicação registrado");

        if (resultado.FaturaPendente is { } fatura)
        {
            assinatura.UrlFaturaPendente = fatura.Url;
            assinatura.VencimentoFaturaPendente = fatura.Vencimento;
            assinatura.ValorFaturaPendente = fatura.Valor;
        }
        else
        {
            assinatura.LimparFaturaPendente();
        }

        _db.TenantIdOverrideDeSistema = tenantId;
        try
        {
            _auditLogWriter.Registrar(OperacaoHistorico, "Assinatura", tenantId, new
            {
                antes,
                depois = new { Status = assinatura.Status.ToString(), assinatura.AtrasadaDesde, assinatura.UltimoPagamentoEm, assinatura.UrlFaturaPendente, assinatura.VencimentoFaturaPendente },
                Mudancas = mudancas,
                OperadorId = operadorId
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }

        _logger.LogWarning("Conciliação com o Asaas corrigiu a conta {TenantId}: {Mudancas}", tenantId, string.Join("; ", mudancas));
        AgendarAvisos(tenantId);

        if (premioRegistrado
            || (assinatura.Status == StatusAssinatura.Ativa && await RegistroPremiosIndicacao.TemPendentesAsync(_db, tenantId, CancellationToken.None)))
            AgendarPremios(tenantId);

        return new ConciliacaoContaResponse(true, "Corrigido pelo Asaas: " + string.Join("; ", mudancas) + ".", mudancas);
    }

    /// <summary>Mesmo critério do webhook: avisos de cobrança logo após a mudança, por job.</summary>
    private void AgendarAvisos(Guid tenantId)
    {
        if (!_cobranca.AtivaPara(tenantId) || _cobranca.ContasIsentas.Contains(tenantId))
            return;
        try
        {
            _jobs.Enqueue<IAvisarCobrancaJob>(job => job.ExecutarContaAsync(tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível agendar os avisos de cobrança da conta {TenantId}; ficam para o job diário.", tenantId);
        }
    }

    /// <summary>Mesmo critério do webhook: aplicação no Asaas por job; se falhar, a varredura diária cobre.</summary>
    private void AgendarPremios(Guid tenantId)
    {
        try
        {
            _jobs.Enqueue<IAplicarPremiosIndicacaoJob>(job => job.ExecutarAsync());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível agendar a aplicação dos prêmios de indicação da conta {TenantId}; fica para a varredura diária.", tenantId);
        }
    }

    private static string? Limitar(string? texto, int maximo) =>
        texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];
}
