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
/// Assinatura da conta: situação (tela e emissão), reserva de fundador,
/// assinar, trocar de plano e cancelar. Regras de decisão ficam em
/// RegrasCobranca e RegrasFundador; aqui só banco e gateway.
/// </summary>
public sealed class CobrancaService : IVerificadorCobranca, IObterAssinaturaUseCase, IAssinarUseCase, ICancelarAssinaturaUseCase, ITrocarPlanoUseCase, IReservaFundador
{
    // Um "Assinar"/"Trocar plano"/reserva de fundador por vez no processo: o
    // botão pode ser clicado duas vezes e criaria duas assinaturas (ou duas
    // trocas) no Asaas, e duas reservas ao mesmo tempo poderiam passar da
    // última vaga.
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

        // Primeira nota em produção: começa a contar o teste grátis (se o
        // CNPJ ainda não usou o teste em outra conta).
        if (assinatura.Status == StatusAssinatura.Teste && assinatura.TesteIniciadoEm is null)
        {
            var (cnpjJaUsouTeste, cnpjsNovos) = await ConsultarTesteDosCnpjsAsync(tenantId, cancellationToken);
            RegistrarInicioDoTeste(assinatura, cnpjJaUsouTeste, cnpjsNovos, DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            if (!cnpjJaUsouTeste)
                return;
        }

        var situacao = Avaliar(assinatura, DateTimeOffset.UtcNow, DataBrasilia.Hoje());
        if (!situacao.PodeEmitirEmProducao)
            throw new RegraNegocioException(situacao.Bloqueio!);
    }

    // ---------- Tela ----------

    async Task<AssinaturaResponse> IObterAssinaturaUseCase.ExecutarAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantAtual();

        // Quem confirmou o e-mail por um caminho sem reserva (convite, senha
        // redefinida) ganha a vaga aqui, se ainda houver.
        await TentarReservarAsync(tenantId, cancellationToken);

        var assinatura = await ObterOuCriarAsync(tenantId, cancellationToken);
        var isenta = _options.ContasIsentas.Contains(tenantId);
        var cobrancaAtiva = _options.AtivaPara(tenantId);
        var agora = DateTimeOffset.UtcNow;
        var hoje = DataBrasilia.Hoje();

        var situacao = cobrancaAtiva && !isenta
            ? Avaliar(assinatura, agora, hoje)
            : new SituacaoCobranca(true, null, null, assinatura.TesteIniciadoEm?.AddDays(Planos.TesteDias));

        var notasMes = await ContarNotasDoMesAsync(_db, tenantId, cancellationToken);
        var empresas = await ContarEmpresasAtivasAsync(_db, tenantId, cancellationToken);

        // Planos do perfil da conta (empresa ou contador), com o preço de hoje.
        var perfil = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Perfil).FirstAsync(cancellationToken);
        var vagasPorGrupo = new Dictionary<PublicoPlano, int>();
        foreach (var publico in Enum.GetValues<PublicoPlano>())
            vagasPorGrupo[publico] = await _catalogo.VagasFundadorRestantesAsync(publico, cancellationToken);

        var temAssinaturaNoGateway = assinatura.AsaasAssinaturaId is not null && assinatura.Status != StatusAssinatura.Cancelada;
        var planoAtual = assinatura.Plano is { } codigo ? await _catalogo.ObterAsync(codigo, cancellationToken) : null;
        var planoNome = planoAtual?.Nome;
        var reservaValida = ReservaValida(assinatura, agora);

        var ofertas = (await _catalogo.PlanosAVendaAsync(perfil, cancellationToken)).Select(p =>
        {
            var vagasPlano = vagasPorGrupo[p.Publico];
            if (temAssinaturaNoGateway)
            {
                // Quem já assina vê o preço que pagaria trocando para este plano.
                var troca = RegrasTrocaPlano.Preco(planoAtual, assinatura.Fundador, p, vagasPlano);
                return new PlanoOfertaResponse(
                    p.Codigo, p.Nome, p.Descricao, p.MaxEmpresas, p.NotasPorMes,
                    troca.Preco, p.PrecoMensal, troca.Fundador, vagasPlano);
            }
            var fundadorAqui = RegrasFundador.AssinaComoFundador(reservaValida, assinatura.FundadorReservaGrupo, p.Publico, vagasPlano);
            return new PlanoOfertaResponse(
                p.Codigo, p.Nome, p.Descricao, p.MaxEmpresas, p.NotasPorMes,
                fundadorAqui ? p.PrecoFundador : p.PrecoMensal, p.PrecoMensal, fundadorAqui, vagasPlano);
        }).ToList();
        var grupoConta = RegrasFundador.GrupoDoPerfil(perfil);
        var vagas = vagasPorGrupo[grupoConta];
        var podeAssinar = cobrancaAtiva && !isenta && !temAssinaturaNoGateway;

        // Primeira cobrança: a marcada (assinou no teste) ou a que valeria assinando agora.
        DateOnly? primeiraCobranca = temAssinaturaNoGateway
            ? assinatura.Status == StatusAssinatura.Teste ? assinatura.PrimeiraCobrancaEm : null
            : podeAssinar
                ? RegrasCobranca.PrimeiroVencimento(assinatura.Status, assinatura.TesteIniciadoEm, assinatura.AcessoAteAposCancelar, agora, hoje,
                    assinatura.SemTesteGratis).Vencimento
                : null;

        return new AssinaturaResponse(
            cobrancaAtiva, isenta, assinatura.Status, assinatura.Plano, planoNome, assinatura.PrecoMensal, assinatura.Fundador,
            assinatura.TesteIniciadoEm, situacao.TesteTerminaEm,
            situacao.PodeEmitirEmProducao, situacao.Bloqueio, situacao.Aviso,
            notasMes, empresas, assinatura.AtrasadaDesde, assinatura.UrlFaturaPendente,
            AguardandoPrimeiroPagamento: temAssinaturaNoGateway && assinatura.Status == StatusAssinatura.Teste,
            PodeAssinar: podeAssinar,
            PodeCancelar: temAssinaturaNoGateway,
            PodeTrocarPlano: cobrancaAtiva && !isenta && temAssinaturaNoGateway,
            vagas, ofertas,
            PrimeiraCobrancaEm: primeiraCobranca,
            FundadorReservado: reservaValida,
            FundadorReservaAte: reservaValida ? RegrasFundador.ReservaValeAte(assinatura.FundadorReservaSemPrazo, assinatura.TesteIniciadoEm) : null,
            FundadorReservaSemPrazo: reservaValida && assinatura.FundadorReservaSemPrazo,
            CampanhaFundadorAte: await _catalogo.CampanhaFundadorAteAsync(grupoConta, cancellationToken),
            AcessoAteAposCancelar: assinatura.Status == StatusAssinatura.Cancelada ? assinatura.AcessoAteAposCancelar : null,
            SemTesteGratis: assinatura.SemTesteGratis);
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
                throw new RegraNegocioException("Esta conta já tem uma assinatura. Para mudar de plano, use \"Trocar para este plano\" na lista de planos.");

            var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
            // Só planos à venda para o perfil da conta (o mesmo que a tela mostra).
            if (plano is null || !plano.AVenda
                || !(tenant.Perfil == PerfilConta.Contador ? plano.OfertarParaContador : plano.OfertarParaEmpresa))
                throw new RegraNegocioException("Plano inválido ou fora de venda. Atualize a página e escolha outro.");
            var email = _currentUser.UserId is { } usuarioId
                ? await _db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken)
                : null;

            var agora = DateTimeOffset.UtcNow;
            var hoje = DataBrasilia.Hoje();
            var reservaValida = ReservaValida(assinatura, agora);
            var vagas = await _catalogo.VagasFundadorRestantesAsync(plano.Publico, cancellationToken);
            var fundador = RegrasFundador.AssinaComoFundador(reservaValida, assinatura.FundadorReservaGrupo, plano.Publico, vagas);
            var preco = fundador ? plano.PrecoFundador : plano.PrecoMensal;

            // Assinar antes da primeira nota começa o teste: confere antes se o
            // CNPJ já usou o teste em outra conta (aí a fatura vence hoje).
            var cnpjJaUsouTeste = assinatura.SemTesteGratis;
            IReadOnlyList<string> cnpjsNovos = Array.Empty<string>();
            if (assinatura.Status == StatusAssinatura.Teste && assinatura.TesteIniciadoEm is null)
            {
                (var usado, cnpjsNovos) = await ConsultarTesteDosCnpjsAsync(assinatura.TenantId, cancellationToken);
                cnpjJaUsouTeste |= usado;
            }

            // Primeira fatura no fim do teste (ninguém perde dia de teste por assinar cedo).
            var statusAntes = assinatura.Status;
            var (vencimento, iniciarTeste) = RegrasCobranca.PrimeiroVencimento(
                assinatura.Status, assinatura.TesteIniciadoEm, assinatura.AcessoAteAposCancelar, agora, hoje, cnpjJaUsouTeste);
            // Tolerância na primeira fatura: só para quem nunca pagou ou ainda tem dias pagos
            // depois de cancelar (cancelar e assinar de novo não abre outra tolerância).
            var primeiraComTolerancia = statusAntes == StatusAssinatura.Teste
                || (statusAntes == StatusAssinatura.Cancelada && vencimento > hoje);

            // Cliente no Asaas: reaproveita o de uma assinatura anterior (cancelada).
            assinatura.AsaasClienteId ??= await _gateway.CriarClienteAsync(tenant.RazaoSocial, tenant.Documento, email, tenantId.ToString(), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var assinaturaId = await _gateway.CriarAssinaturaMensalAsync(
                assinatura.AsaasClienteId, preco, vencimento,
                $"micoempresa - plano {plano.Nome}{(fundador ? " (preço de fundador)" : "")}",
                tenantId.ToString(), cancellationToken);

            assinatura.AsaasAssinaturaId = assinaturaId;
            assinatura.Plano = plano.Codigo;
            assinatura.PrecoMensal = preco;
            assinatura.Fundador = fundador;
            assinatura.AssinadaEm = agora;
            assinatura.CanceladaEm = null;
            assinatura.AcessoAteAposCancelar = null;
            assinatura.AtrasadaDesde = null;
            assinatura.PrimeiraCobrancaEm = primeiraComTolerancia ? vencimento : null;
            if (iniciarTeste)
                RegistrarInicioDoTeste(assinatura, cnpjJaUsouTeste, cnpjsNovos, agora);
            // A reserva de fundador acaba aqui (usada ou não): cancelar depois perde o preço.
            LimparReserva(assinatura);
            // Continua "Teste" até o Asaas confirmar o primeiro pagamento (webhook).
            if (assinatura.Status == StatusAssinatura.Cancelada)
                assinatura.Status = StatusAssinatura.Teste;

            try
            {
                assinatura.UrlFaturaPendente = await _gateway.ObterUrlFaturaPendenteAsync(assinaturaId, cancellationToken);
                if (assinatura.UrlFaturaPendente is not null)
                {
                    assinatura.VencimentoFaturaPendente = vencimento;
                    assinatura.ValorFaturaPendente = preco;
                }
            }
            catch (Exception ex)
            {
                // A fatura chega pelo webhook PAYMENT_CREATED; não desfaz a assinatura por isso.
                _logger.LogWarning(ex, "Não foi possível obter a fatura da assinatura {AssinaturaId} logo após criar.", assinaturaId);
            }

            _auditLogWriter.Registrar("Assinar", "Assinatura", assinatura.Id,
                new { plano = plano.Nome, preco, fundador, fundadorPelaReserva = reservaValida, primeiroVencimento = vencimento, assinaturaId });
            await _db.SaveChangesAsync(cancellationToken);

            return new AssinarResponse(assinatura.UrlFaturaPendente);
        }
        finally
        {
            TravaAssinar.Release();
        }
    }

    // ---------- Trocar plano ----------

    async Task<TrocarPlanoResponse> ITrocarPlanoUseCase.ExecutarAsync(TrocarPlanoRequest request, CancellationToken cancellationToken)
    {
        var tenantId = TenantAtual();
        if (!_options.AtivaPara(tenantId))
            throw new RegraNegocioException("Estamos em beta: o uso é gratuito e ainda não há assinatura para trocar.");
        if (_options.ContasIsentas.Contains(tenantId))
            throw new RegraNegocioException("Esta conta é isenta de cobrança.");

        await TravaAssinar.WaitAsync(cancellationToken);
        try
        {
            var assinatura = await ObterOuCriarAsync(tenantId, cancellationToken);
            if (assinatura.AsaasAssinaturaId is null || assinatura.Status == StatusAssinatura.Cancelada)
                throw new RegraNegocioException("Esta conta não tem assinatura ativa. Escolha um plano e assine.");

            var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
            var novo = await _catalogo.ObterAsync(request.Plano, cancellationToken);
            // Só planos à venda para o perfil da conta (o mesmo que a tela mostra).
            if (novo is null || !novo.AVenda
                || !(tenant.Perfil == PerfilConta.Contador ? novo.OfertarParaContador : novo.OfertarParaEmpresa))
                throw new RegraNegocioException("Plano inválido ou fora de venda. Atualize a página e escolha outro.");

            var atual = assinatura.Plano is { } codigo ? await _catalogo.ObterAsync(codigo, cancellationToken) : null;
            var empresas = await ContarEmpresasAtivasAsync(_db, tenantId, cancellationToken);
            var vagas = await _catalogo.VagasFundadorRestantesAsync(novo.Publico, cancellationToken);

            var troca = RegrasTrocaPlano.Calcular(atual, assinatura.Fundador, novo, vagas, empresas);

            await _gateway.AlterarValorAssinaturaAsync(assinatura.AsaasAssinaturaId, troca.Preco,
                $"micoempresa - plano {novo.Nome}{(troca.Fundador ? " (preço de fundador)" : "")}", cancellationToken);

            var descricao = RegrasTrocaPlano.Descrever(atual?.Nome, assinatura.PrecoMensal, novo.Nome, troca.Preco);
            _auditLogWriter.Registrar("TrocarPlano", "Assinatura", assinatura.Id, new
            {
                de = atual?.Nome,
                precoAnterior = assinatura.PrecoMensal,
                fundadorAnterior = assinatura.Fundador,
                para = novo.Nome,
                preco = troca.Preco,
                fundador = troca.Fundador
            });

            assinatura.Plano = novo.Codigo;
            assinatura.PrecoMensal = troca.Preco;
            assinatura.Fundador = troca.Fundador;
            // A fatura em aberto foi atualizada no Asaas (updatePendingPayments).
            if (assinatura.UrlFaturaPendente is not null)
                assinatura.ValorFaturaPendente = troca.Preco;
            await _db.SaveChangesAsync(cancellationToken);

            return new TrocarPlanoResponse(novo.Nome, troca.Preco, troca.Fundador, descricao);
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

        // Até quando a emissão continua: fim do teste ou do mês já pago.
        // Precisa ser lido ANTES de cancelar (depois o Asaas não informa mais).
        var acessoAte = await AcessoAposCancelarAsync(assinatura, cancellationToken);

        await _gateway.CancelarAssinaturaAsync(assinatura.AsaasAssinaturaId, cancellationToken);

        assinatura.Status = StatusAssinatura.Cancelada;
        assinatura.CanceladaEm = DateTimeOffset.UtcNow;
        assinatura.AcessoAteAposCancelar = acessoAte;
        assinatura.PrimeiraCobrancaEm = null;
        assinatura.LimparFaturaPendente();
        LimparReserva(assinatura);
        _auditLogWriter.Registrar("CancelarAssinatura", "Assinatura", assinatura.Id,
            new { assinatura.AsaasAssinaturaId, acessoAte, fundadorPerdido = assinatura.Fundador });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Último dia com emissão em produção depois de cancelar: no teste, o fim
    /// do teste; ativa, a véspera do próximo vencimento (o mês já pago); em
    /// atraso, nenhum. Falha ao consultar o Asaas = sem dias extras.
    /// </summary>
    private async Task<DateOnly?> AcessoAposCancelarAsync(Assinatura assinatura, CancellationToken cancellationToken)
    {
        var hoje = DataBrasilia.Hoje();
        if (assinatura.Status == StatusAssinatura.Teste)
        {
            if (assinatura.TesteIniciadoEm is not { } inicio)
                return null;
            var termina = inicio.AddDays(Planos.TesteDias);
            return DateTimeOffset.UtcNow < termina ? DiaEmBrasilia(termina) : null;
        }

        if (assinatura.Status != StatusAssinatura.Ativa || assinatura.AsaasAssinaturaId is null)
            return null;

        try
        {
            var proximo = await _gateway.ObterProximoVencimentoAsync(assinatura.AsaasAssinaturaId, cancellationToken);
            return proximo is { } p && p.AddDays(-1) >= hoje ? p.AddDays(-1) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível ler o próximo vencimento da assinatura {AssinaturaId}; cancela sem dias extras.", assinatura.AsaasAssinaturaId);
            return null;
        }
    }

    // ---------- Reserva de fundador ----------

    public async Task<bool> TentarReservarAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_options.ContasIsentas.Contains(tenantId))
            return false;

        // Checagens baratas antes da trava (a tela Assinatura passa por aqui).
        var atual = await _db.Assinaturas.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Select(a => new { a.FundadorReservadoEm, a.Fundador, a.AssinadaEm })
            .FirstOrDefaultAsync(cancellationToken);
        if (atual is not null && (atual.FundadorReservadoEm is not null || atual.Fundador || atual.AssinadaEm is not null))
            return false;

        var tenant = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Ativo, t.BloqueadoEm, t.Perfil })
            .FirstOrDefaultAsync(cancellationToken);
        if (tenant is null || !tenant.Ativo || tenant.BloqueadoEm is not null)
            return false;
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.TenantId == tenantId && u.EmailConfirmed, cancellationToken))
            return false;

        var grupo = RegrasFundador.GrupoDoPerfil(tenant.Perfil);

        await TravaAssinar.WaitAsync(cancellationToken);
        try
        {
            // Sem vaga (ou campanha encerrada), nem cria a assinatura da conta.
            var vagas = await _catalogo.VagasFundadorRestantesAsync(grupo, cancellationToken);
            if (vagas <= 0)
                return false;

            var assinatura = await ObterOuCriarAsync(tenantId, cancellationToken);
            if (!RegrasFundador.PodeReservar(emailConfirmado: true, contaAtiva: true, isenta: false,
                    assinatura.FundadorReservadoEm, assinatura.Fundador, assinatura.AssinadaEm, vagas))
                return false;

            assinatura.FundadorReservadoEm = DateTimeOffset.UtcNow;
            assinatura.FundadorReservaGrupo = grupo;
            // Beta (cobrança ainda desligada para a conta): vale sem prazo.
            assinatura.FundadorReservaSemPrazo = !_options.AtivaPara(tenantId);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Vaga de fundador reservada para a conta {TenantId} ({Grupo}, sem prazo: {SemPrazo}).",
                tenantId, grupo, assinatura.FundadorReservaSemPrazo);
            return true;
        }
        finally
        {
            TravaAssinar.Release();
        }
    }

    public async Task<int> ReservarPendentesAsync(CancellationToken cancellationToken)
    {
        // Mais antigas primeiro: quem entrou antes leva a vaga.
        var candidatas = await _db.Tenants.AsNoTracking()
            .Where(t => t.Ativo && t.BloqueadoEm == null)
            .Where(t => _db.Users.Any(u => u.TenantId == t.Id && u.EmailConfirmed))
            .Where(t => !_db.Assinaturas.Any(a => a.TenantId == t.Id
                && (a.FundadorReservadoEm != null || a.Fundador || a.AssinadaEm != null)))
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var feitas = 0;
        foreach (var tenantId in candidatas)
        {
            if (await TentarReservarAsync(tenantId, cancellationToken))
                feitas++;
        }
        return feitas;
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

    internal static SituacaoCobranca Avaliar(Assinatura assinatura, DateTimeOffset agora, DateOnly hoje) =>
        RegrasCobranca.Avaliar(assinatura.Status, assinatura.TesteIniciadoEm, assinatura.PrimeiraCobrancaEm,
            assinatura.AtrasadaDesde, assinatura.AcessoAteAposCancelar, agora, hoje, assinatura.SemTesteGratis);

    /// <summary>
    /// Um teste por CNPJ: algum CNPJ das empresas da conta já usou o teste em
    /// OUTRA conta? Devolve também os CNPJs ainda não registrados. Conta de
    /// contador não entra (cadastra CNPJs de clientes, que podem ter conta
    /// própria): nunca perde o teste e não registra nada.
    /// </summary>
    private async Task<(bool JaUsado, IReadOnlyList<string> Novos)> ConsultarTesteDosCnpjsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var perfil = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Perfil).FirstOrDefaultAsync(cancellationToken);
        if (perfil == PerfilConta.Contador)
            return (false, Array.Empty<string>());

        // Ativas e desativadas: desativar a empresa não devolve o teste.
        var cnpjs = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.Cnpj)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (cnpjs.Count == 0)
            return (false, Array.Empty<string>());

        var registrados = await _db.TestesGratisCnpj.AsNoTracking()
            .Where(t => cnpjs.Contains(t.Cnpj))
            .ToListAsync(cancellationToken);
        var jaUsado = registrados.Any(r => r.TenantId != tenantId);
        var novos = cnpjs.Except(registrados.Select(r => r.Cnpj)).ToList();
        return (jaUsado, novos);
    }

    /// <summary>Marca o início do teste (ou a falta dele) e registra os CNPJs que passam a ter usado o teste.</summary>
    private void RegistrarInicioDoTeste(Assinatura assinatura, bool cnpjJaUsouTeste, IReadOnlyList<string> cnpjsNovos, DateTimeOffset agora)
    {
        assinatura.TesteIniciadoEm = agora;
        assinatura.SemTesteGratis = cnpjJaUsouTeste;
        foreach (var cnpj in cnpjsNovos)
            _db.TestesGratisCnpj.Add(new TesteGratisCnpj { Cnpj = cnpj, TenantId = assinatura.TenantId, IniciadoEm = agora });

        if (cnpjJaUsouTeste)
            _logger.LogInformation("Conta {TenantId} sem teste grátis: um CNPJ dela já usou o teste em outra conta.", assinatura.TenantId);
    }

    internal static bool ReservaValida(Assinatura assinatura, DateTimeOffset agora) =>
        RegrasFundador.ReservaValida(assinatura.FundadorReservadoEm, assinatura.FundadorReservaSemPrazo,
            assinatura.Fundador, assinatura.Status, assinatura.TesteIniciadoEm, agora);

    private static void LimparReserva(Assinatura assinatura)
    {
        assinatura.FundadorReservadoEm = null;
        assinatura.FundadorReservaGrupo = null;
        assinatura.FundadorReservaSemPrazo = false;
    }

    private static DateOnly DiaEmBrasilia(DateTimeOffset quando) =>
        DateOnly.FromDateTime(quando.ToUniversalTime().AddHours(-3).DateTime);

    /// <summary>Notas autorizadas em produção no mês corrente (Brasília) — o "notas/mês" dos planos.</summary>
    internal static Task<int> ContarNotasDoMesAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var inicioMes = InicioDoMesUtc();
        return db.NotasFiscais.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(n => n.TenantId == tenantId && n.TipoAmbiente == TipoAmbiente.Producao
                             && n.Status == NfseStatus.Autorizada && n.DataEmissao >= inicioMes, cancellationToken);
    }

    /// <summary>Empresas ativas da conta — o "empresas" dos planos.</summary>
    internal static Task<int> ContarEmpresasAtivasAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken) =>
        db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && e.Ativo, cancellationToken);

    private static DateTimeOffset InicioDoMesUtc()
    {
        var hoje = DataBrasilia.Hoje();
        return new DateTimeOffset(hoje.Year, hoje.Month, 1, 0, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
    }
}
