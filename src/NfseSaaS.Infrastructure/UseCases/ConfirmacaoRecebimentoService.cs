using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// CR1: link do [Confirmar recebimento] assinado com a proteção de dados do
/// ASP.NET, com a validade do modelo de e-mail. Leva só a conta e a nota —
/// quem confirma digita o próprio e-mail na página.
/// </summary>
public sealed class LinkConfirmacaoRecebimentoService : ILinkConfirmacaoRecebimento
{
    private const string Finalidade = "NfseSaaS.ConfirmacaoRecebimento.v1";
    private readonly ITimeLimitedDataProtector _protetor;
    private readonly EmailOptions _emailOptions;

    public LinkConfirmacaoRecebimentoService(IDataProtectionProvider protecao, IOptions<EmailOptions> emailOptions)
    {
        _protetor = protecao.CreateProtector(Finalidade).ToTimeLimitedDataProtector();
        _emailOptions = emailOptions.Value;
    }

    private string BaseUrl => string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
        ? $"https://{DocumentosLegais.Site}"
        : _emailOptions.AppBaseUrl.TrimEnd('/');

    public string Exemplo => $"{BaseUrl}/recebimento/exemplo";

    public string Gerar(Guid tenantId, Guid nfseId, int dias)
    {
        var token = _protetor.Protect($"{tenantId:N}{nfseId:N}", TimeSpan.FromDays(RegrasConfirmacaoRecebimento.ValidarDias(dias)));
        return $"{BaseUrl}/recebimento/confirmar?t={Uri.EscapeDataString(token)}";
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

/// <summary>CR1: ver IConfirmacaoRecebimentoUseCase.</summary>
public sealed class ConfirmacaoRecebimentoService : IConfirmacaoRecebimentoUseCase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly ILinkConfirmacaoRecebimento _link;

    public ConfirmacaoRecebimentoService(AppDbContext db, ILinkConfirmacaoRecebimento link)
    {
        _db = db;
        _link = link;
    }

    private sealed record NotaDoLink(Guid TenantId, Guid NfseId, string Empresa, string Numero, decimal Valor, DateOnly Competencia);

    /// <summary>Página pública: a nota do link (de qualquer conta, por isso IgnoreQueryFilters + conta conferida).</summary>
    private async Task<NotaDoLink?> NotaAsync(string? token, CancellationToken cancellationToken)
    {
        if (_link.Ler(token) is not { } dados)
            return null;
        var nota = await (
            from n in _db.NotasFiscais.AsNoTracking().IgnoreQueryFilters()
            join e in _db.Empresas.AsNoTracking().IgnoreQueryFilters() on n.EmpresaId equals e.Id
            where n.Id == dados.NfseId && n.TenantId == dados.TenantId
            select new { n.TenantId, n.Id, Empresa = e.NomeFantasia ?? e.RazaoSocial, n.NumeroNfse, n.NumeroDps, n.ValorServico, n.DataCompetencia })
            .FirstOrDefaultAsync(cancellationToken);
        return nota is null
            ? null
            : new NotaDoLink(nota.TenantId, nota.Id, nota.Empresa,
                string.IsNullOrWhiteSpace(nota.NumeroNfse) ? nota.NumeroDps.ToString("00000", PtBr) : nota.NumeroNfse,
                nota.ValorServico, nota.DataCompetencia);
    }

    private Task<List<ConfirmacaoRecebimentoNota>> ConfirmacoesAsync(Guid tenantId, Guid nfseId, CancellationToken cancellationToken) =>
        _db.ConfirmacoesRecebimentoNota.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.NfseId == nfseId)
            .OrderBy(c => c.ConfirmadoEm)
            .ToListAsync(cancellationToken);

    public async Task<ContextoConfirmacaoRecebimento?> AbrirAsync(string? token, CancellationToken cancellationToken)
    {
        if (await NotaAsync(token, cancellationToken) is not { } nota)
            return null;
        var confirmacoes = await ConfirmacoesAsync(nota.TenantId, nota.NfseId, cancellationToken);
        return new ContextoConfirmacaoRecebimento(
            nota.Empresa, nota.Numero, nota.Valor.ToString("C", PtBr), nota.Competencia.ToString("MM/yyyy", PtBr),
            confirmacoes.Select(c => new ConfirmacaoRecebimentoPublica(RegrasConfirmacaoRecebimento.Mascarar(c.Email), c.ConfirmadoEm)).ToList());
    }

    public async Task<ResultadoConfirmacaoRecebimento> ConfirmarAsync(string? token, string? email, string? ip, CancellationToken cancellationToken)
    {
        var nota = await NotaAsync(token, cancellationToken)
            ?? throw new RegraNegocioException(RegrasConfirmacaoRecebimento.MensagemLinkInvalido);
        var endereco = RegrasSaidaCopia.NormalizarOuErro(email);

        // Destinatários de todos os envios desta nota (o "Reenviar" também conta).
        var envios = await _db.EmailLogs.AsNoTracking().IgnoreQueryFilters()
            .Where(l => l.TenantId == nota.TenantId && l.NfseId == nota.NfseId && l.Tipo == TipoEmailCatalogo.NfseTomador)
            .Select(l => new { l.Destinatario, l.Copias })
            .ToListAsync(cancellationToken);
        var destinatarios = RegrasConfirmacaoRecebimento.Destinatarios(envios.Select(e => (e.Destinatario, e.Copias)));
        if (!destinatarios.TryGetValue(endereco, out var principal))
            throw new RegraNegocioException(RegrasConfirmacaoRecebimento.MensagemForaDaLista);

        var mascarado = RegrasConfirmacaoRecebimento.Mascarar(endereco);
        var jaConfirmou = await _db.ConfirmacoesRecebimentoNota.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == nota.TenantId && c.NfseId == nota.NfseId && c.Email == endereco, cancellationToken);
        if (jaConfirmou)
            return new ResultadoConfirmacaoRecebimento(nota.Empresa, mascarado, true);

        _db.TenantIdOverrideDeSistema = nota.TenantId;
        try
        {
            _db.ConfirmacoesRecebimentoNota.Add(new ConfirmacaoRecebimentoNota
            {
                TenantId = nota.TenantId,
                NfseId = nota.NfseId,
                Email = endereco,
                Principal = principal,
                Ip = string.IsNullOrWhiteSpace(ip) ? null : ip.Length > 45 ? ip[..45] : ip,
                ConfirmadoEm = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dois cliques ao mesmo tempo: o índice único segura; vale a primeira.
            _db.ChangeTracker.Clear();
            return new ResultadoConfirmacaoRecebimento(nota.Empresa, mascarado, true);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }

        return new ResultadoConfirmacaoRecebimento(nota.Empresa, mascarado, false);
    }

    public async Task<IReadOnlyList<ConfirmacaoRecebimentoResponse>> ListarDaNotaAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        // Nota de outra conta: 404, como no detalhe da nota (o filtro de conta
        // já escondia as confirmações; agora também não confirma que a nota existe).
        if (!await _db.NotasFiscais.AsNoTracking().AnyAsync(n => n.Id == nfseId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        return await _db.ConfirmacoesRecebimentoNota.AsNoTracking()
            .Where(c => c.NfseId == nfseId)
            .OrderBy(c => c.ConfirmadoEm)
            .Select(c => new ConfirmacaoRecebimentoResponse(c.Email, c.Principal, c.Ip, c.ConfirmadoEm))
            .ToListAsync(cancellationToken);
    }
}
