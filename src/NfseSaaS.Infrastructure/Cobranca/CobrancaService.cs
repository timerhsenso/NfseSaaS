using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Assinatura da conta: situação (tela e emissão), assinar e cancelar.
/// Regras de decisão ficam em RegrasCobranca; aqui só banco e gateway.
/// </summary>
public sealed class CobrancaService : IVerificadorCobranca, IObterAssinaturaUseCase, IAssinarUseCase, ICancelarAssinaturaUseCase
{
    // Um "Assinar" por vez no processo: o botão pode ser clicado duas vezes
    // e criaria duas assinaturas no Asaas.
    private static readonly SemaphoreSlim TravaAssinar = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IGatewayCobranca _gateway;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ICatalogoPlanos _catalogo;
    private readonly CobrancaOptions _options;
    private readonly ILogger<CobrancaService> _logger;

    public CobrancaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IGatewayCobranca gateway,
        IAuditLogWriter auditLogWriter,
        ICatalogoPlanos catalogo,
        IOptions<CobrancaOptions> options,
        ILogger<CobrancaService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _auditLogWriter = auditLogWriter;
        _catalogo = catalogo;
        _options = options.Value;
        _logger = logger;
    }

    // ---------- Emissão ----------

    public async Task GarantirPodeEmitirEmProducaoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_options.AtivaPara(tenantId) || _options.ContasIsentas.Contains(tenantId))
            return;

        var assinatura = await ObterOuCriarAsync(tenantId, cancellationToken);

        // Primeira nota em produção: começa a contar o teste grátis.
        if (assinatura.Status == StatusAssinatura.Teste && assinatura.TesteIniciadoEm is null)
        {
            assinatura.TesteIniciadoEm = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var situacao = await AvaliarAsync(assinatura, cancellationToken);
        if (!situacao.PodeEmitirEmProducao)
            throw new RegraNegocioException(situacao.Bloqueio!);
    }

    // ---------- Tela ----------

    async Task<AssinaturaResponse> IObterAssinaturaUseCase.ExecutarAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantAtual();
        var assinatura = await ObterOuCriarAsync(tenantId, cancellationToken);
        var isenta = _options.ContasIsentas.Contains(tenantId);
        var cobrancaAtiva = _options.AtivaPara(tenantId);

        var notasTeste = await NotasDoTesteAsync(assinatura, cancellationToken);
        var situacao = cobrancaAtiva && !isenta
            ? RegrasCobranca.Avaliar(assinatura.Status, assinatura.TesteIniciadoEm, notasTeste, assinatura.AtrasadaDesde, DateTimeOffset.UtcNow, DataBrasilia.Hoje())
            : new SituacaoCobranca(true, null, null, assinatura.TesteIniciadoEm?.AddDays(Planos.TesteDias));

        var inicioMes = InicioDoMesUtc();
        var notasMes = await _db.NotasFiscais.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(n => n.TenantId == tenantId && n.TipoAmbiente == TipoAmbiente.Producao
                             && n.Status == NfseStatus.Autorizada && n.DataEmissao >= inicioMes, cancellationToken);
        var empresas = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && e.Ativo, cancellationToken);

        // Planos do perfil da conta (empresa ou contador), com o preço de hoje.
        var perfil = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Perfil).FirstAsync(cancellationToken);
        var vagasPorGrupo = new Dictionary<PublicoPlano, int>();
        foreach (var publico in Enum.GetValues<PublicoPlano>())
            vagasPorGrupo[publico] = await _catalogo.VagasFundadorRestantesAsync(publico, cancellationToken);

        var ofertas = (await _catalogo.PlanosAVendaAsync(perfil, cancellationToken)).Select(p =>
        {
            var vagasPlano = vagasPorGrupo[p.Publico];
            return new PlanoOfertaResponse(
                p.Codigo, p.Nome, p.Descricao, p.MaxEmpresas, p.NotasPorMes,
                vagasPlano > 0 ? p.PrecoFundador : p.PrecoMensal, p.PrecoMensal, vagasPlano > 0, vagasPlano);
        }).ToList();
        var vagas = vagasPorGrupo[perfil == PerfilConta.Contador ? PublicoPlano.Escritorios : PublicoPlano.Empresas];

        var temAssinaturaNoGateway = assinatura.AsaasAssinaturaId is not null && assinatura.Status != StatusAssinatura.Cancelada;
        var planoNome = assinatura.Plano is { } codigo ? (await _catalogo.ObterAsync(codigo, cancellationToken))?.Nome : null;

        return new AssinaturaResponse(
            cobrancaAtiva, isenta, assinatura.Status, assinatura.Plano, planoNome, assinatura.PrecoMensal, assinatura.Fundador,
            assinatura.TesteIniciadoEm, situacao.TesteTerminaEm, notasTeste, Planos.TesteNotas,
            situacao.PodeEmitirEmProducao, situacao.Bloqueio, situacao.Aviso,
            notasMes, empresas, assinatura.AtrasadaDesde, assinatura.UrlFaturaPendente,
            AguardandoPrimeiroPagamento: temAssinaturaNoGateway && assinatura.Status == StatusAssinatura.Teste,
            PodeAssinar: cobrancaAtiva && !isenta && !temAssinaturaNoGateway,
            PodeCancelar: temAssinaturaNoGateway,
            vagas, ofertas);
    }

    // ---------- Assinar ----------

    async Task<AssinarResponse> IAssinarUseCase.ExecutarAsync(AssinarRequest request, CancellationToken cancellationToken)
    {
        var tenantId = TenantAtual();
        if (!_options.AtivaPara(tenantId))
            throw new RegraNegocioException("Estamos em beta: o uso é gratuito e ainda não há assinatura para contratar.");
        if (_options.ContasIsentas.Contains(tenantId))
            throw new RegraNegocioException("Esta conta é isenta de cobrança.");

        var plano = await _catalogo.ObterAsync(request.Plano, cancellationToken);

        await TravaAssinar.WaitAsync(cancellationToken);
        try
        {
            var assinatura = await ObterOuCriarAsync(tenantId, cancellationToken);
            if (assinatura.AsaasAssinaturaId is not null && assinatura.Status != StatusAssinatura.Cancelada)
                throw new RegraNegocioException("Esta conta já tem uma assinatura. Para trocar de plano, fale com o suporte.");

            var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
            // Só planos à venda para o perfil da conta (o mesmo que a tela mostra).
            if (plano is null || !plano.AVenda
                || !(tenant.Perfil == PerfilConta.Contador ? plano.OfertarParaContador : plano.OfertarParaEmpresa))
                throw new RegraNegocioException("Plano inválido ou fora de venda. Atualize a página e escolha outro.");
            var email = _currentUser.UserId is { } usuarioId
                ? await _db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken)
                : null;

            var fundador = await _catalogo.VagasFundadorRestantesAsync(plano.Publico, cancellationToken) > 0;
            var preco = fundador ? plano.PrecoFundador : plano.PrecoMensal;

            // Cliente no Asaas: reaproveita o de uma assinatura anterior (cancelada).
            assinatura.AsaasClienteId ??= await _gateway.CriarClienteAsync(tenant.RazaoSocial, tenant.Documento, email, tenantId.ToString(), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var assinaturaId = await _gateway.CriarAssinaturaMensalAsync(
                assinatura.AsaasClienteId, preco, DataBrasilia.Hoje(),
                $"micoempresa - plano {plano.Nome}{(fundador ? " (preço de fundador)" : "")}",
                tenantId.ToString(), cancellationToken);

            assinatura.AsaasAssinaturaId = assinaturaId;
            assinatura.Plano = plano.Codigo;
            assinatura.PrecoMensal = preco;
            assinatura.Fundador = fundador;
            assinatura.AssinadaEm = DateTimeOffset.UtcNow;
            assinatura.CanceladaEm = null;
            assinatura.AtrasadaDesde = null;
            // Continua "Teste" até o Asaas confirmar o primeiro pagamento (webhook).
            if (assinatura.Status == StatusAssinatura.Cancelada)
                assinatura.Status = StatusAssinatura.Teste;

            try
            {
                assinatura.UrlFaturaPendente = await _gateway.ObterUrlFaturaPendenteAsync(assinaturaId, cancellationToken);
                if (assinatura.UrlFaturaPendente is not null)
                {
                    // A primeira fatura vence hoje (primeiroVencimento acima).
                    assinatura.VencimentoFaturaPendente = DataBrasilia.Hoje();
                    assinatura.ValorFaturaPendente = preco;
                }
            }
            catch (Exception ex)
            {
                // A fatura chega pelo webhook PAYMENT_CREATED; não desfaz a assinatura por isso.
                _logger.LogWarning(ex, "Não foi possível obter a fatura da assinatura {AssinaturaId} logo após criar.", assinaturaId);
            }

            _auditLogWriter.Registrar("Assinar", "Assinatura", assinatura.Id, new { plano = plano.Nome, preco, fundador, assinaturaId });
            await _db.SaveChangesAsync(cancellationToken);

            return new AssinarResponse(assinatura.UrlFaturaPendente);
        }
        finally
        {
            TravaAssinar.Release();
        }
    }

    // ---------- Cancelar ----------

    async Task ICancelarAssinaturaUseCase.ExecutarAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantAtual();
        var assinatura = await ObterOuCriarAsync(tenantId, cancellationToken);
        if (assinatura.AsaasAssinaturaId is null || assinatura.Status == StatusAssinatura.Cancelada)
            throw new RegraNegocioException("Não há assinatura ativa para cancelar.");

        await _gateway.CancelarAssinaturaAsync(assinatura.AsaasAssinaturaId, cancellationToken);

        assinatura.Status = StatusAssinatura.Cancelada;
        assinatura.CanceladaEm = DateTimeOffset.UtcNow;
        assinatura.LimparFaturaPendente();
        _auditLogWriter.Registrar("CancelarAssinatura", "Assinatura", assinatura.Id, new { assinatura.AsaasAssinaturaId });
        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---------- Apoio ----------

    private Guid TenantAtual() =>
        _currentTenant.TenantId ?? throw new InvalidOperationException("Conta não identificada.");

    private async Task<Assinatura> ObterOuCriarAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var assinatura = await _db.Assinaturas.FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        if (assinatura is not null)
            return assinatura;

        assinatura = new Assinatura { TenantId = tenantId };
        _db.Assinaturas.Add(assinatura);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return assinatura;
        }
        catch (DbUpdateException)
        {
            // Criada ao mesmo tempo em outra requisição (índice único por conta).
            _db.Entry(assinatura).State = EntityState.Detached;
            return await _db.Assinaturas.FirstAsync(a => a.TenantId == tenantId, cancellationToken);
        }
    }

    private async Task<SituacaoCobranca> AvaliarAsync(Assinatura assinatura, CancellationToken cancellationToken) =>
        RegrasCobranca.Avaliar(assinatura.Status, assinatura.TesteIniciadoEm, await NotasDoTesteAsync(assinatura, cancellationToken),
            assinatura.AtrasadaDesde, DateTimeOffset.UtcNow, DataBrasilia.Hoje());

    private Task<int> NotasDoTesteAsync(Assinatura assinatura, CancellationToken cancellationToken) =>
        ContarNotasDoTesteAsync(_db, assinatura, cancellationToken);

    /// <summary>Notas autorizadas em produção desde o início do teste (rejeitadas não contam).</summary>
    internal static async Task<int> ContarNotasDoTesteAsync(AppDbContext db, Assinatura assinatura, CancellationToken cancellationToken)
    {
        if (assinatura.TesteIniciadoEm is not { } inicio)
            return 0;

        var inicioUtc = inicio.ToUniversalTime();
        return await db.NotasFiscais.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(n => n.TenantId == assinatura.TenantId && n.TipoAmbiente == TipoAmbiente.Producao
                             && n.Status == NfseStatus.Autorizada && n.DataEmissao >= inicioUtc, cancellationToken);
    }

    private static DateTimeOffset InicioDoMesUtc()
    {
        var hoje = DataBrasilia.Hoje();
        return new DateTimeOffset(hoje.Year, hoje.Month, 1, 0, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
    }
}
