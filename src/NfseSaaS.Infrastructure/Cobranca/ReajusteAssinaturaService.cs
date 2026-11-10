using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Reajuste do preço de tabela para quem já assina (B2). O operador escolhe o
/// plano e a data (pelo menos 30 dias à frente); o novo valor é o preço de
/// tabela do plano naquela data (Plataforma › Planos › Novo preço). Quem
/// entra recebe o aviso por e-mail na hora; na data, o job grava o valor no
/// Asaas (a fatura em aberto daquela data em diante também muda) e na
/// assinatura. Fundador, assinatura com menos de 12 meses (ou reajustada há
/// menos de 12) e quem já paga igual ou mais ficam de fora (RegrasReajusteAssinatura).
///
/// Alterar o valor no Asaas é idempotente (grava o mesmo valor de novo), então
/// uma passada que caia no meio é só repetida no dia seguinte.
/// </summary>
public sealed class ReajusteAssinaturaService : IReajusteAssinantesUseCase, IAplicarReajustesAssinaturaUseCase
{
    private readonly AppDbContext _db;
    private readonly IGatewayCobranca _gateway;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly CobrancaOptions _cobranca;
    private readonly EmailOptions _email;
    private readonly SuporteOptions _suporte;
    private readonly ILogger<ReajusteAssinaturaService> _logger;

    public ReajusteAssinaturaService(
        AppDbContext db,
        IGatewayCobranca gateway,
        IEmailQueueService emailQueue,
        IAuditLogWriter auditLogWriter,
        IOptions<CobrancaOptions> cobranca,
        IOptions<EmailOptions> email,
        IOptions<SuporteOptions> suporte,
        ILogger<ReajusteAssinaturaService> logger)
    {
        _db = db;
        _gateway = gateway;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
        _cobranca = cobranca.Value;
        _email = email.Value;
        _suporte = suporte.Value;
        _logger = logger;
    }

    // ---------- Prévia e agendamento (operador) ----------

    public async Task<PreviaReajusteResponse> PreviaAsync(Guid planoId, DateOnly aplicarEm, CancellationToken cancellationToken)
    {
        var (plano, precoNovo, linhas) = await AvaliarAsync(planoId, aplicarEm, cancellationToken);
        var contas = linhas
            .Select(l => new LinhaPreviaReajuste(l.Assinatura.TenantId, l.Conta, l.Assinatura.PrecoMensal, l.Assinatura.Fundador, l.Motivo is null, l.Motivo))
            .OrderBy(l => !l.Entra).ThenBy(l => l.Conta)
            .ToList();
        return new PreviaReajusteResponse(plano.Nome, precoNovo, aplicarEm, RegrasReajusteAssinatura.DataMinima(DataBrasilia.Hoje()),
            contas.Count(c => c.Entra), contas);
    }

    public async Task<AgendarReajusteResponse> AgendarAsync(Guid planoId, AgendarReajusteRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        var motivo = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();
        if (motivo is { Length: > 200 })
            throw new RegraNegocioException("O motivo tem no máximo 200 caracteres.");

        var (plano, precoNovo, linhas) = await AvaliarAsync(planoId, request.AplicarEm, cancellationToken);
        var entram = linhas.Where(l => l.Motivo is null).ToList();
        if (entram.Count == 0)
            throw new RegraNegocioException("Nenhuma assinatura deste plano entra no reajuste nesta data. Veja os motivos na prévia.");

        var loteId = Guid.NewGuid();
        var reajustes = entram.Select(l => new ReajusteAssinatura
        {
            TenantId = l.Assinatura.TenantId,
            AssinaturaId = l.Assinatura.Id,
            LoteId = loteId,
            Plano = plano.Codigo,
            PrecoAnterior = l.Assinatura.PrecoMensal!.Value,
            PrecoNovo = precoNovo,
            AplicarEm = request.AplicarEm,
            Motivo = motivo,
            CriadoPorUsuarioId = operadorId
        }).ToList();
        _db.ReajustesAssinatura.AddRange(reajustes);

        // Fica no Histórico do operador (conta de quem clicou).
        _auditLogWriter.Registrar("AgendarReajusteAssinantes", "Plano", plano.Id, new
        {
            plano = plano.Nome,
            precoNovo,
            aplicarEm = request.AplicarEm,
            agendados = reajustes.Count,
            foraDoReajuste = linhas.Count - reajustes.Count,
            loteId,
            motivo
        });
        await _db.SaveChangesAsync(cancellationToken);

        // Avisos depois de gravar: e-mail na fila sem reajuste gravado seria pior
        // que reajuste gravado sem e-mail (este aparece como "sem aviso" na tela).
        var semDestinatario = 0;
        foreach (var reajuste in reajustes)
        {
            if (await AvisarAsync(reajuste, plano.Nome, cancellationToken))
                reajuste.AvisadoEm = DateTimeOffset.UtcNow;
            else
                semDestinatario++;
        }
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reajuste do plano {Plano} para {Preco} em {Data}: {Agendados} agendado(s), {Fora} fora, lote {Lote}.",
            plano.Nome, precoNovo, request.AplicarEm, reajustes.Count, linhas.Count - reajustes.Count, loteId);

        return new AgendarReajusteResponse(loteId, reajustes.Count, linhas.Count - reajustes.Count, semDestinatario);
    }

    public async Task<IReadOnlyList<ReajusteAssinaturaResponse>> ListarAsync(Guid planoId, CancellationToken cancellationToken)
    {
        var codigo = await _db.Planos.AsNoTracking().Where(p => p.Id == planoId).Select(p => (int?)p.Codigo).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Plano não encontrado.");

        return await (
            from r in _db.ReajustesAssinatura.AsNoTracking()
            join t in _db.Tenants.AsNoTracking() on r.TenantId equals t.Id
            where r.Plano == codigo
            orderby r.CreatedAt descending, t.RazaoSocial
            select new ReajusteAssinaturaResponse(
                r.Id, r.LoteId, t.NomeFantasia ?? t.RazaoSocial, r.PrecoAnterior, r.PrecoNovo, r.AplicarEm, r.Status,
                r.AvisadoEm, r.AplicadoEm, r.Motivo, r.Detalhe, r.CreatedAt))
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CancelarLoteAsync(Guid loteId, Guid? operadorId, CancellationToken cancellationToken)
    {
        var agendados = await _db.ReajustesAssinatura
            .Where(r => r.LoteId == loteId && r.Status == StatusReajusteAssinatura.Agendado)
            .ToListAsync(cancellationToken);
        if (agendados.Count == 0)
            throw new RegraNegocioException("Não há reajuste agendado neste lote (já aplicado ou cancelado).");

        foreach (var r in agendados)
            Cancelar(r, "Cancelado pelo operador antes da data.");

        _auditLogWriter.Registrar("CancelarReajusteAssinantes", "ReajusteAssinatura", loteId, new { loteId, cancelados = agendados.Count });
        await _db.SaveChangesAsync(cancellationToken);
        return agendados.Count;
    }

    // ---------- Aplicar na data (job) ----------

    public async Task<ResultadoReajustesAssinatura> ExecutarAsync(CancellationToken cancellationToken)
    {
        var hoje = DataBrasilia.Hoje();
        var devidos = await _db.ReajustesAssinatura
            .Where(r => r.Status == StatusReajusteAssinatura.Agendado && r.AplicarEm <= hoje)
            .OrderBy(r => r.AplicarEm)
            .ToListAsync(cancellationToken);
        if (devidos.Count == 0)
            return new ResultadoReajustesAssinatura(0, 0, 0);

        var nomes = await _db.Planos.AsNoTracking().ToDictionaryAsync(p => p.Codigo, p => p.Nome, cancellationToken);
        int aplicados = 0, cancelados = 0, falhas = 0;

        foreach (var reajuste in devidos)
        {
            var assinatura = await _db.Assinaturas.FirstOrDefaultAsync(a => a.Id == reajuste.AssinaturaId, cancellationToken);
            var motivo = assinatura is null
                ? "A assinatura não existe mais."
                : RegrasReajusteAssinatura.MotivoNaoAplicar(Situacao(assinatura, null, false, hoje), reajuste.Plano, reajuste.PrecoAnterior);

            if (motivo is not null)
            {
                Cancelar(reajuste, motivo);
                await _db.SaveChangesAsync(cancellationToken);
                cancelados++;
                continue;
            }

            try
            {
                var nome = nomes.GetValueOrDefault(reajuste.Plano, $"#{reajuste.Plano}");
                await _gateway.AlterarValorAssinaturaAsync(assinatura!.AsaasAssinaturaId!, reajuste.PrecoNovo,
                    $"micoempresa - plano {nome}", cancellationToken);

                assinatura.PrecoMensal = reajuste.PrecoNovo;
                // A fatura em aberto (vencimento de hoje em diante) mudou junto no Asaas.
                if (assinatura.UrlFaturaPendente is not null && assinatura.VencimentoFaturaPendente is { } venc && venc >= hoje)
                    assinatura.ValorFaturaPendente = reajuste.PrecoNovo;

                reajuste.Status = StatusReajusteAssinatura.Aplicado;
                reajuste.AplicadoEm = DateTimeOffset.UtcNow;
                reajuste.Detalhe = null;
                reajuste.UpdatedAt = reajuste.AplicadoEm;

                // Fica no Histórico da conta reajustada.
                _db.TenantIdOverrideDeSistema = reajuste.TenantId;
                try
                {
                    _auditLogWriter.Registrar("ReajusteAssinaturaAplicado", "Assinatura", assinatura.Id, new
                    {
                        plano = nome,
                        de = reajuste.PrecoAnterior,
                        para = reajuste.PrecoNovo,
                        reajuste.AplicarEm
                    });
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                finally
                {
                    _db.TenantIdOverrideDeSistema = null;
                }

                aplicados++;
                _logger.LogInformation("Reajuste {ReajusteId} aplicado na conta {TenantId}: {De} → {Para}.",
                    reajuste.Id, reajuste.TenantId, reajuste.PrecoAnterior, reajuste.PrecoNovo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                falhas++;
                reajuste.Status = StatusReajusteAssinatura.Agendado;
                reajuste.AplicadoEm = null;
                reajuste.Tentativas++;
                reajuste.Detalhe = Cortar(ex.Message, 300);
                reajuste.UpdatedAt = DateTimeOffset.UtcNow;
                if (reajuste.Tentativas >= RegrasReajusteAssinatura.MaxTentativas)
                    reajuste.Status = StatusReajusteAssinatura.Erro;
                // Nada do que falhou foi gravado na assinatura.
                _db.Entry(assinatura!).State = EntityState.Unchanged;
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogError(ex, "Reajuste {ReajusteId} da conta {TenantId} falhou (tentativa {Tentativa}).",
                    reajuste.Id, reajuste.TenantId, reajuste.Tentativas);
            }
        }

        return new ResultadoReajustesAssinatura(aplicados, cancelados, falhas);
    }

    // ---------- Apoio ----------

    private sealed record Linha(Assinatura Assinatura, string Conta, string? Motivo);

    private async Task<(Plano Plano, decimal PrecoNovo, List<Linha> Linhas)> AvaliarAsync(Guid planoId, DateOnly aplicarEm, CancellationToken cancellationToken)
    {
        var hoje = DataBrasilia.Hoje();
        if (RegrasReajusteAssinatura.ProblemaNaData(aplicarEm, hoje) is { } problema)
            throw new RegraNegocioException(problema);

        var plano = await _db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Plano não encontrado.");

        // Preço de tabela que vale NA DATA do reajuste (pode ser um já agendado).
        var precoNovo = await _db.PrecosPlanos.AsNoTracking()
            .Where(p => p.PlanoId == planoId && p.VigenteDesde <= aplicarEm)
            .OrderByDescending(p => p.VigenteDesde)
            .Select(p => (decimal?)p.PrecoMensal)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RegraNegocioException($"O plano {plano.Nome} não tem preço em {RegrasReajusteAssinatura.Dia(aplicarEm)}.");

        var assinaturas = await _db.Assinaturas.AsNoTracking()
            .Where(a => a.Plano == plano.Codigo && a.Status != StatusAssinatura.Cancelada && a.AsaasAssinaturaId != null)
            .ToListAsync(cancellationToken);
        var ids = assinaturas.Select(a => a.Id).ToList();
        var tenantIds = assinaturas.Select(a => a.TenantId).ToList();

        var contas = await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.NomeFantasia ?? t.RazaoSocial, cancellationToken);
        var agendados = (await _db.ReajustesAssinatura.AsNoTracking()
            .Where(r => ids.Contains(r.AssinaturaId) && r.Status == StatusReajusteAssinatura.Agendado)
            .Select(r => r.AssinaturaId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var ultimos = await _db.ReajustesAssinatura.AsNoTracking()
            .Where(r => ids.Contains(r.AssinaturaId) && r.Status == StatusReajusteAssinatura.Aplicado)
            .GroupBy(r => r.AssinaturaId)
            .Select(g => new { g.Key, Ultimo = g.Max(r => r.AplicarEm) })
            .ToDictionaryAsync(x => x.Key, x => x.Ultimo, cancellationToken);

        var linhas = assinaturas.Select(a =>
        {
            var situacao = Situacao(a, ultimos.TryGetValue(a.Id, out var u) ? u : null, agendados.Contains(a.Id), hoje);
            return new Linha(a, contas.GetValueOrDefault(a.TenantId, "—"),
                RegrasReajusteAssinatura.MotivoForaDoReajuste(situacao, plano.Codigo, precoNovo, aplicarEm));
        }).ToList();

        return (plano, precoNovo, linhas);
    }

    private SituacaoReajuste Situacao(Assinatura a, DateOnly? ultimoReajuste, bool temAgendado, DateOnly hoje) => new(
        a.Status,
        a.AsaasAssinaturaId is not null,
        _cobranca.AtivaPara(a.TenantId) && !_cobranca.ContasIsentas.Contains(a.TenantId)
            && !RegrasCobranca.Isenta(false, a.IsentaPelaPlataforma, a.IsentaAte, hoje),
        a.Plano,
        a.PrecoMensal,
        a.Fundador,
        a.AssinadaEm is { } em ? DateOnly.FromDateTime(em.UtcDateTime.AddHours(-3)) : null,
        ultimoReajuste,
        temAgendado);

    /// <summary>Aviso aos administradores com e-mail confirmado. false = ninguém para avisar.</summary>
    private async Task<bool> AvisarAsync(ReajusteAssinatura reajuste, string planoNome, CancellationToken cancellationToken)
    {
        var chave = RegrasReajusteAssinatura.ChaveAviso(reajuste.Id);
        var link = string.IsNullOrWhiteSpace(_email.AppBaseUrl) ? null : $"{_email.AppBaseUrl.TrimEnd('/')}/Assinatura";
        var (assunto, corpo) = MensagensReajusteAssinatura.Montar(planoNome, reajuste.PrecoAnterior, reajuste.PrecoNovo, reajuste.AplicarEm, link, _suporte.TextoContato);

        // Quem agenda é o operador (outra conta): leitura sem o filtro global,
        // e o EmailLog gravado como da conta avisada (override de escrita).
        var destinatarios = await DestinatariosAviso.AdministradoresConfirmadosDeOutraContaAsync(_db, reajuste.TenantId, cancellationToken);
        _db.TenantIdOverrideDeSistema = reajuste.TenantId;
        try
        {
            if (destinatarios.Count == 0)
                return false;

            foreach (var destinatario in destinatarios)
                await _emailQueue.EnfileirarAsync(
                    new EmailParaEnfileirar(TipoEmailCatalogo.ReajusteAssinatura, null, destinatario, assunto, corpo, ChaveDeduplicacao: chave),
                    cancellationToken);
            return true;
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
    }

    private static void Cancelar(ReajusteAssinatura reajuste, string detalhe)
    {
        reajuste.Status = StatusReajusteAssinatura.Cancelado;
        reajuste.Detalhe = detalhe;
        reajuste.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string Cortar(string texto, int tamanho) => texto.Length <= tamanho ? texto : texto[..tamanho];

    /// <summary>
    /// Troca de plano ou cancelamento pela tela: o reajuste avisado deixa de
    /// valer na hora (senão a tela Assinatura continuaria anunciando).
    /// </summary>
    internal static async Task CancelarAgendadosDaAssinaturaAsync(AppDbContext db, Guid assinaturaId, string detalhe, CancellationToken cancellationToken)
    {
        var agendados = await db.ReajustesAssinatura
            .Where(r => r.AssinaturaId == assinaturaId && r.Status == StatusReajusteAssinatura.Agendado)
            .ToListAsync(cancellationToken);
        foreach (var r in agendados)
            Cancelar(r, detalhe);
    }
}
