using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// RC1: link do rodapé assinado com a proteção de dados do ASP.NET (com
/// validade). Leva só a conta e a nota — quem vai sair é confirmado depois,
/// pelo próprio e-mail.
/// </summary>
public sealed class LinkSaidaCopiaService : ILinkSaidaCopia
{
    private const string Finalidade = "NfseSaaS.SaidaCopia.v1";
    private readonly ITimeLimitedDataProtector _protetor;
    private readonly EmailOptions _emailOptions;

    public LinkSaidaCopiaService(IDataProtectionProvider protecao, IOptions<EmailOptions> emailOptions)
    {
        _protetor = protecao.CreateProtector(Finalidade).ToTimeLimitedDataProtector();
        _emailOptions = emailOptions.Value;
    }

    internal string BaseUrl => string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
        ? $"https://{DocumentosLegais.Site}"
        : _emailOptions.AppBaseUrl.TrimEnd('/');

    public string Gerar(Guid tenantId, Guid nfseId)
    {
        var token = _protetor.Protect($"{tenantId:N}{nfseId:N}", RegrasSaidaCopia.ValidadeLink);
        return $"{BaseUrl}/copia/sair?t={Uri.EscapeDataString(token)}";
    }

    public (Guid TenantId, Guid NfseId)? Ler(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1000)
            return null;
        try
        {
            var texto = _protetor.Unprotect(token.Trim());
            return texto.Length == 64
                   && Guid.TryParseExact(texto[..32], "N", out var tenant)
                   && Guid.TryParseExact(texto[32..], "N", out var nota)
                ? (tenant, nota)
                : null;
        }
        catch (CryptographicException)
        {
            return null; // adulterado ou vencido
        }
    }
}

/// <summary>RC1: ver ISaidaCopiaEmailUseCase.</summary>
public sealed class SaidaCopiaEmailService : ISaidaCopiaEmailUseCase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly LinkSaidaCopiaService _link;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;

    public SaidaCopiaEmailService(AppDbContext db, LinkSaidaCopiaService link, IEmailQueueService emailQueue, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _link = link;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
    }

    private sealed record NotaDoLink(Guid TenantId, Guid NfseId, Guid ClienteId, string Empresa, string? EmailEmpresa, string? NumeroNfse, int NumeroDps);

    /// <summary>Página pública: a nota do link (de qualquer conta, por isso IgnoreQueryFilters + conta conferida).</summary>
    private async Task<NotaDoLink?> NotaAsync(string? token, CancellationToken cancellationToken)
    {
        if (_link.Ler(token) is not { } dados)
            return null;
        return await (
            from n in _db.NotasFiscais.AsNoTracking().IgnoreQueryFilters()
            join e in _db.Empresas.AsNoTracking().IgnoreQueryFilters() on n.EmpresaId equals e.Id
            where n.Id == dados.NfseId && n.TenantId == dados.TenantId
            select new NotaDoLink(n.TenantId, n.Id, n.ClienteId, e.NomeFantasia ?? e.RazaoSocial, e.Email, n.NumeroNfse, n.NumeroDps))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ContextoSaidaCopia?> AbrirAsync(string? token, CancellationToken cancellationToken) =>
        await NotaAsync(token, cancellationToken) is { } nota ? new ContextoSaidaCopia(nota.Empresa) : null;

    public async Task<ResultadoPedidoSaidaCopia> PedirAsync(string? token, string? email, string? ip, CancellationToken cancellationToken)
    {
        var nota = await NotaAsync(token, cancellationToken)
            ?? throw new RegraNegocioException("Este link expirou ou não é válido. Use o link de um e-mail de nota mais recente.");
        var endereco = RegrasSaidaCopia.NormalizarOuErro(email);

        var cliente = await _db.Clientes.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.Id == nota.ClienteId && c.TenantId == nota.TenantId)
            .Select(c => new { c.Email, c.EmailsCopia })
            .FirstOrDefaultAsync(cancellationToken);
        if (cliente is null)
            return ResultadoPedidoSaidaCopia.ConfirmacaoEnviada;

        // O principal nunca sai por aqui (mesmo que também esteja nas cópias).
        if (RegrasSaidaCopia.EhPrincipal(cliente.Email, endereco))
            return ResultadoPedidoSaidaCopia.Principal;

        // Não é cópia (ou já saiu): mesma resposta, sem revelar a lista.
        if (!RegrasSaidaCopia.EhCopia(cliente.EmailsCopia, endereco))
            return ResultadoPedidoSaidaCopia.ConfirmacaoEnviada;

        var desde = DateTimeOffset.UtcNow.AddDays(-1);
        var pedidosHoje = await _db.SaidasCopiaEmail.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == nota.TenantId && s.ClienteId == nota.ClienteId && s.Email == endereco && s.CreatedAt >= desde, cancellationToken);
        if (pedidosHoje >= RegrasSaidaCopia.MaximoPedidosPorDia)
            return ResultadoPedidoSaidaCopia.ConfirmacaoEnviada;

        var (codigo, hash) = RegrasSaidaCopia.NovoCodigo();
        _db.TenantIdOverrideDeSistema = nota.TenantId;
        try
        {
            _db.SaidasCopiaEmail.Add(new SaidaCopiaEmail
            {
                TenantId = nota.TenantId,
                ClienteId = nota.ClienteId,
                NfseId = nota.NfseId,
                Email = endereco,
                CodigoHash = hash,
                ExpiraEm = DateTimeOffset.UtcNow.Add(RegrasSaidaCopia.ValidadeConfirmacao),
                IpPedido = Ip(ip)
            });

            var link = $"{_link.BaseUrl}/copia/confirmar?c={Uri.EscapeDataString(codigo)}";
            var empresa = WebUtility.HtmlEncode(nota.Empresa);
            var corpo = $"""
                <p>Recebemos um pedido para que <strong>{WebUtility.HtmlEncode(endereco)}</strong> pare de receber, como cópia, as notas fiscais de <strong>{empresa}</strong>.</p>
                <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#2563eb;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none">Confirmar e sair das cópias</a></p>
                <p style="color:#64748b;font-size:13px">O link vale por 24 horas. Se não foi você quem pediu, ignore este e-mail: nada muda.</p>
                <p style="color:#64748b;font-size:12px">Enviado pela plataforma micoempresa em nome de {empresa}.</p>
                """;
            await _emailQueue.EnfileirarAsync(new EmailParaEnfileirar(
                TipoEmailCatalogo.SaidaCopiaConfirmacao, null, endereco,
                $"Confirme: parar de receber as notas de {nota.Empresa} como cópia", corpo, Segredos: new[] { codigo }), cancellationToken);

            _auditLogWriter.Registrar("PedidoSaidaCopia", "Cliente", nota.ClienteId, new { email = endereco, nfseId = nota.NfseId, ip = Ip(ip) });
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }

        return ResultadoPedidoSaidaCopia.ConfirmacaoEnviada;
    }

    private async Task<(SaidaCopiaEmail Saida, string Empresa, string? EmailEmpresa, Guid? EmpresaId, string? Numero)?> SaidaAsync(
        string? codigo, bool rastrear, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 200)
            return null;
        var hash = RegrasSaidaCopia.Hash(codigo.Trim());
        var consulta = _db.SaidasCopiaEmail.IgnoreQueryFilters().Where(s => s.CodigoHash == hash);
        var saida = rastrear ? await consulta.FirstOrDefaultAsync(cancellationToken) : await consulta.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (saida is null)
            return null;

        var empresa = await (
            from n in _db.NotasFiscais.AsNoTracking().IgnoreQueryFilters()
            join e in _db.Empresas.AsNoTracking().IgnoreQueryFilters() on n.EmpresaId equals e.Id
            where n.Id == saida.NfseId && n.TenantId == saida.TenantId
            select new { Nome = e.NomeFantasia ?? e.RazaoSocial, e.Email, EmpresaId = (Guid?)e.Id, n.NumeroNfse, n.NumeroDps })
            .FirstOrDefaultAsync(cancellationToken);
        var nome = empresa?.Nome
            ?? await _db.Tenants.AsNoTracking().Where(t => t.Id == saida.TenantId).Select(t => t.RazaoSocial).FirstOrDefaultAsync(cancellationToken)
            ?? "o fornecedor";
        var numero = empresa is null ? null : string.IsNullOrWhiteSpace(empresa.NumeroNfse) ? empresa.NumeroDps.ToString(PtBr) : empresa.NumeroNfse;
        return (saida, nome, empresa?.Email, empresa?.EmpresaId, numero);
    }

    public async Task<ConfirmacaoSaidaCopia?> ObterConfirmacaoAsync(string? codigo, CancellationToken cancellationToken)
    {
        if (await SaidaAsync(codigo, false, cancellationToken) is not { } x)
            return null;
        var confirmada = x.Saida.Status == StatusSaidaCopia.Confirmada;
        return new ConfirmacaoSaidaCopia(x.Empresa, RegrasSaidaCopia.Mascarar(x.Saida.Email), confirmada,
            !confirmada && x.Saida.ExpiraEm < DateTimeOffset.UtcNow);
    }

    public async Task<ConfirmacaoSaidaCopia> ConfirmarAsync(string? codigo, string? ip, CancellationToken cancellationToken)
    {
        var x = await SaidaAsync(codigo, true, cancellationToken)
            ?? throw new RegraNegocioException("Link de confirmação inválido. Peça de novo pelo link do e-mail da nota.");
        var saida = x.Saida;
        if (saida.Status == StatusSaidaCopia.Confirmada)
            return new ConfirmacaoSaidaCopia(x.Empresa, RegrasSaidaCopia.Mascarar(saida.Email), true, false);
        if (saida.ExpiraEm < DateTimeOffset.UtcNow)
            throw new RegraNegocioException("Este link de confirmação expirou (vale 24 horas). Peça de novo pelo link do e-mail da nota.");

        _db.TenantIdOverrideDeSistema = saida.TenantId;
        try
        {
            var cliente = await _db.Clientes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == saida.ClienteId && c.TenantId == saida.TenantId, cancellationToken)
                ?? throw new RegraNegocioException("O cadastro deste cliente não existe mais: você já não recebe estas notas.");

            var antes = cliente.EmailsCopia;
            // O principal nunca sai (se virou principal depois do pedido, fica).
            if (!RegrasSaidaCopia.EhPrincipal(cliente.Email, saida.Email))
            {
                cliente.EmailsCopia = RegrasSaidaCopia.Remover(cliente.EmailsCopia, saida.Email);
                if (cliente.EmailsCopia != antes)
                    cliente.UpdatedAt = DateTimeOffset.UtcNow;
            }

            saida.Status = StatusSaidaCopia.Confirmada;
            saida.ConfirmadoEm = DateTimeOffset.UtcNow;
            saida.IpConfirmacao = Ip(ip);
            saida.UpdatedAt = DateTimeOffset.UtcNow;

            _auditLogWriter.Registrar("SaidaCopiaConfirmada", "Cliente", cliente.Id,
                new { email = saida.Email, nota = x.Numero, nfseId = saida.NfseId, ip = saida.IpConfirmacao, copiasAntes = antes, copiasDepois = cliente.EmailsCopia });

            // Aviso ao fornecedor: administradores da conta e o e-mail da empresa.
            // Página pública (sem conta logada): a consulta filtra a conta à mão.
            var destinatarios = DestinatariosAviso.Normalizar(
                (await DestinatariosAviso.AdministradoresConfirmadosDeOutraContaAsync(_db, saida.TenantId, cancellationToken)).Append(x.EmailEmpresa));
            var corpo = $"""
                <p><strong>{WebUtility.HtmlEncode(saida.Email)}</strong> pediu para não receber mais, como cópia, as notas do cliente <strong>{WebUtility.HtmlEncode(cliente.Apelido)}</strong>{(x.Numero is null ? "" : $" (pedido feito a partir da nota {WebUtility.HtmlEncode(x.Numero)})")}.</p>
                <p>O e-mail foi tirado das cópias do cliente e o pedido ficou registrado no histórico. Se alguém incluir esse e-mail de novo, o sistema avisa antes.</p>
                <p style="color:#64748b;font-size:13px">Confirmado em {DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy 'às' HH:mm", PtBr)} pelo próprio destinatário.</p>
                """;
            foreach (var destinatario in destinatarios)
                await _emailQueue.EnfileirarAsync(new EmailParaEnfileirar(TipoEmailCatalogo.SaidaCopiaAviso, null, destinatario,
                    $"{saida.Email} saiu das cópias das notas de {cliente.Apelido}", corpo,
                    ChaveDeduplicacao: $"saida-copia:{saida.Id:N}"), cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }

        return new ConfirmacaoSaidaCopia(x.Empresa, RegrasSaidaCopia.Mascarar(saida.Email), true, false);
    }

    public async Task<IReadOnlyList<SaidaCopiaClienteResponse>> ListarDoClienteAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var saidas = await (
            from s in _db.SaidasCopiaEmail.AsNoTracking()
            where s.ClienteId == clienteId && s.Status == StatusSaidaCopia.Confirmada
            join n in _db.NotasFiscais.AsNoTracking() on s.NfseId equals n.Id into notas
            from n in notas.DefaultIfEmpty()
            orderby s.ConfirmadoEm descending
            select new { s.Email, s.ConfirmadoEm, Numero = n == null ? null : n.NumeroNfse, Dps = n == null ? (int?)null : n.NumeroDps })
            .ToListAsync(cancellationToken);
        return saidas
            .GroupBy(s => s.Email)
            .Select(g => g.First())
            .Select(s => new SaidaCopiaClienteResponse(s.Email, s.ConfirmadoEm!.Value, s.Numero ?? s.Dps?.ToString(PtBr)))
            .ToList();
    }

    private static string? Ip(string? ip) => string.IsNullOrWhiteSpace(ip) ? null : ip.Length > 45 ? ip[..45] : ip;
}
