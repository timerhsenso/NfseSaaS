using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Ações de cobrança do operador (Plataforma › Contas). Só valem para conta
/// sem assinatura viva no Asaas: depois que existe fatura, quem manda é o
/// Asaas (prorrogar ou isentar aqui não mudaria a fatura de lá). Cada ação
/// fica no Histórico da conta afetada, com o operador e o motivo.
/// </summary>
public sealed class CobrancaOperadorService : ICobrancaOperadorUseCase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<CobrancaOperadorService> _logger;

    public CobrancaOperadorService(AppDbContext db, IAuditLogWriter auditLogWriter, ILogger<CobrancaOperadorService> logger)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    public async Task<CobrancaOperadorResponse> ProrrogarTesteAsync(Guid tenantId, ProrrogarTesteRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        var assinatura = await AssinaturaSemFaturaAsync(tenantId, "prorrogar o teste", cancellationToken);
        if (assinatura.SemTesteGratis)
            throw new RegraNegocioException("Esta conta está sem teste porque o CNPJ já usou o teste em outra conta. Use \"Liberar novo teste\".");

        var total = assinatura.TesteDiasExtras + request.Dias;
        if (total > LimitesCobrancaOperador.DiasExtrasTotalMaximo)
            throw new RegraNegocioException(
                $"Esta conta já tem {assinatura.TesteDiasExtras} dia(s) extra(s); o máximo somado é {LimitesCobrancaOperador.DiasExtrasTotalMaximo}. Para mais, use \"Liberar novo teste\" ou a isenção.");

        DateTimeOffset? novoFim = assinatura.TesteIniciadoEm?.AddDays(RegrasCobranca.DiasDeTeste(total));
        if (novoFim is { } fim && fim <= DateTimeOffset.UtcNow)
            throw new RegraNegocioException(
                $"Com mais {request.Dias} dia(s), o teste terminaria em {Data(fim)}, que já passou. Use mais dias ou \"Liberar novo teste\".");

        var antes = assinatura.TesteDiasExtras;
        assinatura.TesteDiasExtras = total;
        await SalvarComHistoricoAsync(tenantId, "ProrrogarTeste",
            new { dias = request.Dias, antes, depois = total, novoFim, Motivo = request.Motivo.Trim(), OperadorId = operadorId }, cancellationToken);

        _logger.LogInformation("Teste da conta {TenantId} prorrogado em {Dias} dia(s) pelo operador {OperadorId}.", tenantId, request.Dias, operadorId);
        return new CobrancaOperadorResponse(novoFim is { } f
            ? $"Teste prorrogado: agora termina em {Data(f)}."
            : $"Teste prorrogado: quando começar, terá {RegrasCobranca.DiasDeTeste(total)} dias.");
    }

    public async Task<CobrancaOperadorResponse> LiberarTesteAsync(Guid tenantId, LiberarTesteRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        var assinatura = await AssinaturaSemFaturaAsync(tenantId, "liberar um novo teste", cancellationToken);
        if (assinatura.Status == StatusAssinatura.Cancelada)
            throw new RegraNegocioException("A conta já foi assinante (assinatura cancelada): novo teste não se aplica. Use a isenção, se for o caso.");

        var agora = DateTimeOffset.UtcNow;
        var antes = new { assinatura.TesteIniciadoEm, assinatura.TesteDiasExtras, assinatura.SemTesteGratis };
        assinatura.TesteIniciadoEm = agora;
        assinatura.TesteDiasExtras = 0;
        assinatura.SemTesteGratis = false;

        // Os CNPJs da conta passam a constar como "teste usado por ESTA conta",
        // para a regra de um teste por CNPJ não bloquear de novo.
        var cnpjs = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId).Select(e => e.Cnpj).Distinct().ToListAsync(cancellationToken);
        var registrados = await _db.TestesGratisCnpj.Where(t => cnpjs.Contains(t.Cnpj)).ToListAsync(cancellationToken);
        foreach (var r in registrados)
            r.TenantId = tenantId;
        foreach (var cnpj in cnpjs.Except(registrados.Select(r => r.Cnpj)))
            _db.TestesGratisCnpj.Add(new TesteGratisCnpj { Cnpj = cnpj, TenantId = tenantId, IniciadoEm = agora });

        var fim = agora.AddDays(RegrasCobranca.DiasDeTeste(0));
        await SalvarComHistoricoAsync(tenantId, "LiberarNovoTeste",
            new { antes, fim, cnpjs = cnpjs.Count, Motivo = request.Motivo.Trim(), OperadorId = operadorId }, cancellationToken);

        _logger.LogInformation("Novo teste liberado para a conta {TenantId} pelo operador {OperadorId}.", tenantId, operadorId);
        return new CobrancaOperadorResponse($"Novo teste liberado: termina em {Data(fim)}.");
    }

    public async Task<CobrancaOperadorResponse> IsentarAsync(Guid tenantId, IsentarContaRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        var hoje = DataBrasilia.Hoje();
        if (request.Ate is { } ate && ate < hoje)
            throw new RegraNegocioException("A data da isenção já passou. Deixe em branco para isentar sem prazo.");

        var assinatura = await AssinaturaSemFaturaAsync(tenantId, "isentar", cancellationToken);
        assinatura.IsentaPelaPlataforma = true;
        assinatura.IsentaAte = request.Ate;
        assinatura.MotivoIsencao = request.Motivo.Trim();
        await SalvarComHistoricoAsync(tenantId, "IsentarConta",
            new { ate = request.Ate, Motivo = assinatura.MotivoIsencao, OperadorId = operadorId }, cancellationToken);

        _logger.LogInformation("Conta {TenantId} isenta até {Ate} pelo operador {OperadorId}.", tenantId, request.Ate, operadorId);
        return new CobrancaOperadorResponse(request.Ate is { } d
            ? $"Conta isenta até {d.ToString("dd/MM/yyyy", PtBr)}."
            : "Conta isenta sem prazo.");
    }

    public async Task<CobrancaOperadorResponse> RemoverIsencaoAsync(Guid tenantId, Guid? operadorId, CancellationToken cancellationToken)
    {
        var assinatura = await _db.Assinaturas.FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        if (assinatura is null || !assinatura.IsentaPelaPlataforma)
            throw new RegraNegocioException("Esta conta não está isenta pela tela.");

        var antes = new { assinatura.IsentaAte, assinatura.MotivoIsencao };
        assinatura.IsentaPelaPlataforma = false;
        assinatura.IsentaAte = null;
        assinatura.MotivoIsencao = null;
        await SalvarComHistoricoAsync(tenantId, "RemoverIsencao", new { antes, OperadorId = operadorId }, cancellationToken);
        return new CobrancaOperadorResponse("Isenção removida: a conta volta às regras normais de cobrança.");
    }

    /// <summary>Assinatura da conta (criada se faltar), desde que sem assinatura viva no Asaas.</summary>
    private async Task<Assinatura> AssinaturaSemFaturaAsync(Guid tenantId, string acao, CancellationToken cancellationToken)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Conta {tenantId} não encontrada.");

        var assinatura = await _db.Assinaturas.FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        if (assinatura is null)
        {
            assinatura = new Assinatura { TenantId = tenantId, Status = StatusAssinatura.Teste };
            _db.Assinaturas.Add(assinatura);
        }

        if (assinatura.AsaasAssinaturaId is not null && assinatura.Status != StatusAssinatura.Cancelada)
            throw new RegraNegocioException(
                $"Não dá para {acao}: a conta já assinou e as faturas estão no Asaas. Ajuste pelo painel do Asaas (ou peça para cancelar a assinatura antes).");
        return assinatura;
    }

    /// <summary>Grava a mudança e o Histórico na conta afetada (o cliente vê no Histórico dela).</summary>
    private async Task SalvarComHistoricoAsync(Guid tenantId, string acao, object detalhes, CancellationToken cancellationToken)
    {
        _db.TenantIdOverrideDeSistema = tenantId;
        try
        {
            _auditLogWriter.Registrar(acao, "Assinatura", tenantId, detalhes);
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
    }

    private static string Data(DateTimeOffset quando) =>
        quando.ToUniversalTime().AddHours(-3).ToString("dd/MM/yyyy", PtBr);
}
