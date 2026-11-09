using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Configuração da NFS-e da mensalidade (N1). Vazio = DESLIGADO.
/// EmpresaId: a empresa que emite (a LFMS, cadastrada na conta do operador,
/// com certificado). ServicoId: o Serviço dessa empresa usado nas notas — o
/// item da LC 116 (padrão sugerido 01.05), NBS e IBS/CBS ficam nele.
/// </summary>
public sealed class NotaMensalidadeOptions
{
    public const string SectionName = "NotaMensalidade";

    public Guid? EmpresaId { get; set; }

    public Guid? ServicoId { get; set; }

    /// <summary>Opcional: pagamentos registrados antes deste dia não geram nota.</summary>
    public DateOnly? EmitirAPartirDe { get; set; }

    public bool Ligado => EmpresaId is { } e && e != Guid.Empty && ServicoId is { } s && s != Guid.Empty;
}

/// <summary>
/// Job da NFS-e da mensalidade (N1). Cada passada:
/// 1. registra os pagamentos confirmados (eventos PAYMENT_CONFIRMED /
///    PAYMENT_RECEIVED gravados pelo webhook) que ainda não têm linha em
///    notas_mensalidade — um pagamento, uma linha (índice único);
/// 2. pagamento desfeito (estorno/chargeback): antes da nota, não emite;
///    depois, avisa o operador uma vez para cancelar a nota;
/// 3. emite as pendentes e as que falharam (até 8 tentativas), na conta
///    emissora, com o tomador = a conta que pagou. O tomador é procurado
///    pelo CPF/CNPJ da conta entre os clientes da empresa emissora; se não
///    existe, é criado com o endereço da empresa da própria conta (mesmo
///    CNPJ), com envio da nota por e-mail para o administrador. Sem
///    endereço, a nota fica em Erro e o operador cadastra o cliente à mão.
/// A emissão usa a chave de idempotência "mensalidade:{pagamento}": repetir
/// nunca duplica; rejeição que permite reenvio é reenviada automaticamente.
/// Toda falha vira LogError (alerta por e-mail ao operador).
/// </summary>
public sealed class NotasMensalidadeService : INotasMensalidadeUseCase
{
    private static readonly string[] TiposPagamento = ["PAYMENT_CONFIRMED", "PAYMENT_RECEIVED"];

    private static readonly string[] TiposDesfeito =
        ["PAYMENT_REFUNDED", "PAYMENT_REFUND_IN_PROGRESS", "PAYMENT_CHARGEBACK_REQUESTED", "PAYMENT_CHARGEBACK_DISPUTE", "PAYMENT_RECEIVED_IN_CASH_UNDONE"];

    private const int LotePorPassada = 50;

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEmitirNfseUseCase _emitir;
    private readonly NotaMensalidadeOptions _options;
    private readonly CobrancaOptions _cobranca;
    private readonly ILogger<NotasMensalidadeService> _logger;

    public NotasMensalidadeService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        IEmitirNfseUseCase emitir,
        IOptions<NotaMensalidadeOptions> options,
        IOptions<CobrancaOptions> cobranca,
        ILogger<NotasMensalidadeService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _emitir = emitir;
        _options = options.Value;
        _cobranca = cobranca.Value;
        _logger = logger;
    }

    public async Task<ResultadoNotasMensalidade> ExecutarAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        if (!_options.Ligado)
            return new ResultadoNotasMensalidade(true, 0, 0, 0, 0);

        var empresa = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == _options.EmpresaId, cancellationToken);
        var servico = await _db.Servicos.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == _options.ServicoId, cancellationToken);
        if (empresa is null || servico is null || servico.EmpresaId != empresa.Id)
        {
            _logger.LogError("NFS-e da mensalidade: NotaMensalidade:EmpresaId/ServicoId não encontrados ou o serviço não é dessa empresa. Nenhuma nota emitida.");
            return new ResultadoNotasMensalidade(false, 0, 0, 1, 0);
        }

        // Daqui em diante tudo (cliente criado, nota emitida) é da conta emissora.
        _currentTenant.DefinirTenantIdDeSistema(empresa.TenantId);

        var registradas = await RegistrarNovosAsync(empresa.TenantId, agora, cancellationToken);
        var ignoradas = await TratarDesfeitosAsync(cancellationToken);

        var fila = await _db.NotasMensalidade
            .Where(n => (n.Status == StatusNotaMensalidade.Pendente || n.Status == StatusNotaMensalidade.Erro) && n.Tentativas < RegrasNotaMensalidade.MaximoTentativas)
            .OrderBy(n => n.CreatedAt)
            .Take(LotePorPassada)
            .ToListAsync(cancellationToken);

        int emitidas = 0, comErro = 0;
        foreach (var nota in fila)
        {
            if (await EmitirAsync(nota, empresa, servico, cancellationToken))
                emitidas++;
            else
                comErro++;
        }

        if (registradas + emitidas + comErro + ignoradas > 0)
            _logger.LogInformation("NFS-e da mensalidade: {Registradas} pagamento(s) novo(s), {Emitidas} nota(s) emitida(s), {ComErro} com erro, {Ignoradas} ignorada(s).",
                registradas, emitidas, comErro, ignoradas);

        return new ResultadoNotasMensalidade(false, registradas, emitidas, comErro, ignoradas);
    }

    // ---------- 1. Pagamentos novos ----------

    private async Task<int> RegistrarNovosAsync(Guid tenantEmissor, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var desde = agora.AddDays(-RegrasNotaMensalidade.JanelaDias).ToUniversalTime();
        if (_options.EmitirAPartirDe is { } aPartir)
        {
            var inicio = new DateTimeOffset(aPartir.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3)).ToUniversalTime();
            if (inicio > desde)
                desde = inicio;
        }

        var eventos = await _db.EventosCobranca.AsNoTracking()
            .Where(e => TiposPagamento.Contains(e.Tipo) && e.TenantId != null && e.AsaasPagamentoId != null && e.CreatedAt >= desde)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new { PagamentoId = e.AsaasPagamentoId!, TenantId = e.TenantId!.Value, e.Payload, e.CreatedAt })
            .ToListAsync(cancellationToken);
        if (eventos.Count == 0)
            return 0;

        var ids = eventos.Select(e => e.PagamentoId).Distinct().ToList();
        var conhecidos = (await _db.NotasMensalidade.AsNoTracking()
                .Where(n => ids.Contains(n.AsaasPagamentoId))
                .Select(n => n.AsaasPagamentoId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var novas = 0;
        foreach (var e in eventos.GroupBy(e => e.PagamentoId).Select(g => g.First()))
        {
            if (conhecidos.Contains(e.PagamentoId))
                continue;
            // A própria conta emissora (e as isentas) não recebem nota de si mesmas.
            if (e.TenantId == tenantEmissor || _cobranca.ContasIsentas.Contains(e.TenantId) || !_cobranca.AtivaPara(e.TenantId))
                continue;

            var (valor, dia) = LerPagamento(e.Payload, e.CreatedAt);
            if (valor is not > 0)
            {
                _logger.LogError("NFS-e da mensalidade: pagamento {PagamentoId} sem valor no evento do Asaas; nota não registrada.", e.PagamentoId);
                continue;
            }

            _db.NotasMensalidade.Add(new NotaMensalidade { TenantId = e.TenantId, AsaasPagamentoId = e.PagamentoId, Valor = valor.Value, DataPagamento = dia });
            novas++;
        }

        if (novas > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return novas;
    }

    internal static (decimal? Valor, DateOnly Dia) LerPagamento(string payload, DateTimeOffset recebidoEm)
    {
        var dia = DateOnly.FromDateTime(recebidoEm.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("payment", out var p) || p.ValueKind != JsonValueKind.Object)
                return (null, dia);

            decimal? valor = p.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? decimal.Round(d, 2) : null;
            foreach (var campo in new[] { "paymentDate", "clientPaymentDate", "confirmedDate" })
            {
                if (p.TryGetProperty(campo, out var c) && c.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(c.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                {
                    dia = data;
                    break;
                }
            }
            return (valor, dia);
        }
        catch (JsonException)
        {
            return (null, dia);
        }
    }

    // ---------- 2. Pagamentos desfeitos ----------

    private async Task<int> TratarDesfeitosAsync(CancellationToken cancellationToken)
    {
        var abertas = await _db.NotasMensalidade
            .Where(n => n.Status == StatusNotaMensalidade.Pendente || n.Status == StatusNotaMensalidade.Erro
                        || (n.Status == StatusNotaMensalidade.Emitida && n.EstornoAvisadoEm == null))
            .Where(n => _db.EventosCobranca.Any(e => e.AsaasPagamentoId == n.AsaasPagamentoId && TiposDesfeito.Contains(e.Tipo)))
            .ToListAsync(cancellationToken);
        if (abertas.Count == 0)
            return 0;

        var ignoradas = 0;
        var agora = DateTimeOffset.UtcNow;
        foreach (var n in abertas)
        {
            n.UpdatedAt = agora;
            if (n.Status == StatusNotaMensalidade.Emitida)
            {
                n.EstornoAvisadoEm = agora;
                _logger.LogError("NFS-e da mensalidade: o pagamento {PagamentoId} foi desfeito (estorno/chargeback) depois da nota {NfseId}. Cancele a nota na tela de notas da LFMS.",
                    n.AsaasPagamentoId, n.NfseId);
            }
            else
            {
                n.Status = StatusNotaMensalidade.Ignorada;
                n.UltimoErro = "Pagamento desfeito no Asaas antes da emissão.";
                ignoradas++;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ignoradas;
    }

    // ---------- 3. Emissão ----------

    private async Task<bool> EmitirAsync(NotaMensalidade nota, Empresa empresa, Servico servico, CancellationToken cancellationToken)
    {
        nota.Tentativas++;
        nota.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            var clienteId = await ObterOuCriarTomadorAsync(nota.TenantId, empresa.Id, cancellationToken);
            var nomePlano = await (
                from a in _db.Assinaturas.AsNoTracking()
                join p in _db.Planos.AsNoTracking() on a.Plano equals (int?)p.Codigo
                where a.TenantId == nota.TenantId
                select p.Nome).FirstOrDefaultAsync(cancellationToken);

            var resultado = await _emitir.ExecutarAsync(new EmitirNfseRequest(
                empresa.Id, clienteId, servico.Id, nota.Valor,
                RegrasNotaMensalidade.Descricao(nomePlano, nota.DataPagamento, nota.AsaasPagamentoId),
                nota.DataPagamento,
                IdempotencyKey: RegrasNotaMensalidade.ChaveIdempotencia(nota.AsaasPagamentoId)), cancellationToken);

            nota.NfseId = resultado.NfseId;
            if (resultado.Sucesso)
            {
                nota.Status = StatusNotaMensalidade.Emitida;
                nota.EmitidaEm = DateTimeOffset.UtcNow;
                nota.UltimoErro = null;
                await _db.SaveChangesAsync(CancellationToken.None);
                return true;
            }

            Falhou(nota, $"{resultado.CodigoErro} {resultado.MensagemErro}".Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _db.ChangeTracker.Entries().Where(e => e.Entity is not NotaMensalidade).ToList().ForEach(e => e.State = EntityState.Detached);
            Falhou(nota, ex.Message);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
        return false;
    }

    private void Falhou(NotaMensalidade nota, string erro)
    {
        nota.UltimoErro = erro.Length <= 500 ? erro : erro[..500];
        var desistiu = nota.Tentativas >= RegrasNotaMensalidade.MaximoTentativas;
        nota.Status = desistiu ? StatusNotaMensalidade.Ignorada : StatusNotaMensalidade.Erro;
        _logger.LogError("NFS-e da mensalidade do pagamento {PagamentoId} (conta {TenantId}) falhou na tentativa {Tentativa}{Desistiu}: {Erro}",
            nota.AsaasPagamentoId, nota.TenantId, nota.Tentativas, desistiu ? " — tentativas esgotadas, emita à mão" : "", nota.UltimoErro);
    }

    /// <summary>Tomador = a conta que pagou, como cliente da empresa emissora.</summary>
    private async Task<Guid> ObterOuCriarTomadorAsync(Guid tenantPagador, Guid empresaEmissoraId, CancellationToken cancellationToken)
    {
        var conta = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantPagador, cancellationToken);
        // CPF/CNPJ só com letras e números (o CNPJ novo é alfanumérico), como no cadastro.
        var documento = new string(conta.Documento.Where(char.IsLetterOrDigit).ToArray());

        var existente = await _db.Clientes
            .Where(c => c.EmpresaId == empresaEmissoraId && c.CpfCnpj == documento)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existente is { } id)
            return id;

        // Endereço: a empresa da própria conta com o mesmo CNPJ (perfil Empresa).
        var origem = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantPagador && e.Cnpj == documento)
            .FirstOrDefaultAsync(cancellationToken);
        if (origem is null || string.IsNullOrWhiteSpace(origem.CodigoMunicipio) || string.IsNullOrWhiteSpace(origem.Cep)
            || string.IsNullOrWhiteSpace(origem.Logradouro) || string.IsNullOrWhiteSpace(origem.Numero) || string.IsNullOrWhiteSpace(origem.Bairro))
            throw new InvalidOperationException(
                $"Sem endereço para o tomador {documento} ({conta.RazaoSocial}): cadastre-o como cliente da LFMS na tela Clientes; a próxima passada emite.");

        var emailAdmin = await (
            from u in _db.Users.AsNoTracking()
            join g in _db.Grupos.IgnoreQueryFilters().AsNoTracking() on u.GrupoId equals g.Id
            where u.TenantId == tenantPagador && g.TenantId == tenantPagador && g.EhAdministrador && u.Email != null
            orderby u.Email
            select u.Email).FirstOrDefaultAsync(cancellationToken);

        var nome = conta.NomeFantasia ?? conta.RazaoSocial;
        var apelidoUsado = await _db.Clientes.AnyAsync(c => c.EmpresaId == empresaEmissoraId
            && c.Apelido == RegrasNotaMensalidade.Apelido(nome, documento, false, Cliente.TamanhoMaximoApelido), cancellationToken);

        var cliente = new Cliente
        {
            EmpresaId = empresaEmissoraId,
            CpfCnpj = documento,
            Nome = conta.RazaoSocial.Length <= 200 ? conta.RazaoSocial : conta.RazaoSocial[..200],
            Apelido = RegrasNotaMensalidade.Apelido(nome, documento, apelidoUsado, Cliente.TamanhoMaximoApelido),
            Email = emailAdmin?.Trim(),
            EnviarNfsePorEmail = !string.IsNullOrWhiteSpace(emailAdmin),
            CodigoMunicipio = origem.CodigoMunicipio,
            Cep = origem.Cep,
            Logradouro = origem.Logradouro,
            Numero = origem.Numero,
            Complemento = origem.Complemento,
            Bairro = origem.Bairro,
            Uf = origem.Uf
        };
        _db.Clientes.Add(cliente);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("NFS-e da mensalidade: cliente {Documento} ({Nome}) criado na empresa emissora.", documento, conta.RazaoSocial);
        return cliente.Id;
    }
}
