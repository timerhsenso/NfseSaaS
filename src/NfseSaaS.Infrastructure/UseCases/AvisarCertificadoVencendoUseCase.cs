using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Ver IAvisarCertificadoVencendoUseCase. Um e-mail por destinatário, todos
/// com a mesma ChaveDeduplicacao ("cert:{empresa}:{validade}:{faixa}"):
/// achou a chave no EmailLog, aquela faixa já foi avisada.
/// </summary>
public sealed class AvisarCertificadoVencendoUseCase : IAvisarCertificadoVencendoUseCase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan OffsetBrasilia = TimeSpan.FromHours(-3);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IObterStatusCertificadoUseCase _obterStatusCertificado;
    private readonly IEmailQueueService _emailQueue;
    private readonly EmailOptions _emailOptions;

    public AvisarCertificadoVencendoUseCase(
        AppDbContext db,
        ICurrentTenant currentTenant,
        IObterStatusCertificadoUseCase obterStatusCertificado,
        IEmailQueueService emailQueue,
        IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _currentTenant = currentTenant;
        _obterStatusCertificado = obterStatusCertificado;
        _emailQueue = emailQueue;
        _emailOptions = emailOptions.Value;
    }

    public async Task<AvisoCertificadoResult> ExecutarAsync(Guid empresaId, DateOnly hoje, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            throw new InvalidOperationException("Tenant não definido para o aviso de certificado.");

        var empresa = await _db.Empresas.AsNoTracking()
            .Where(e => e.Id == empresaId)
            .Select(e => new { e.RazaoSocial, e.Cnpj, e.Email })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var status = await _obterStatusCertificado.ExecutarAsync(empresaId, cancellationToken);
        if (!status.Existe || status.ValidoAte is not { } validoAte)
            return new AvisoCertificadoResult(false, null, null, Array.Empty<string>(), "Empresa sem certificado.");

        var vencimento = DateOnly.FromDateTime(validoAte.ToOffset(OffsetBrasilia).DateTime);
        var diasRestantes = vencimento.DayNumber - hoje.DayNumber;

        if (FaixaAvisoCertificado.Calcular(diasRestantes) is not { } faixa)
            return new AvisoCertificadoResult(false, diasRestantes, null, Array.Empty<string>(), "Fora das faixas de aviso.");

        var chave = $"cert:{empresaId:N}:{vencimento:yyyyMMdd}:{faixa}";
        if (await _emailQueue.JaEnfileiradoAsync(chave, cancellationToken))
            return new AvisoCertificadoResult(false, diasRestantes, faixa, Array.Empty<string>(), "Faixa já avisada.");

        var destinatarios = await ObterDestinatariosAsync(tenantId, empresa.Email, cancellationToken);
        if (destinatarios.Count == 0)
            return new AvisoCertificadoResult(false, diasRestantes, faixa, destinatarios, "Nenhum destinatário com e-mail.");

        var (assunto, corpoHtml) = MontarMensagem(empresa.RazaoSocial, empresa.Cnpj, vencimento, diasRestantes);

        foreach (var destinatario in destinatarios)
        {
            await _emailQueue.EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.CertificadoVencendo, null, destinatario, assunto, corpoHtml, ChaveDeduplicacao: chave),
                cancellationToken);
        }

        return new AvisoCertificadoResult(true, diasRestantes, faixa, destinatarios, null);
    }

    /// <summary>
    /// Administradores ATIVOS da conta (grupo administrador, convite aceito,
    /// sem bloqueio) e o e-mail cadastrado da Empresa, sem repetir.
    /// AspNetUsers não tem filtro de tenant — o filtro aqui é manual.
    /// </summary>
    private async Task<List<string>> ObterDestinatariosAsync(Guid tenantId, string? emailEmpresa, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;

        var administradores = await (
            from u in _db.Users.AsNoTracking()
            join g in _db.Grupos.AsNoTracking() on u.GrupoId equals g.Id
            where u.TenantId == tenantId
                && g.EhAdministrador
                && u.Email != null
                && (u.ConvidadoEm == null || u.ConviteAceitoEm != null)
                && (u.LockoutEnd == null || u.LockoutEnd < agora)
            select u.Email!).ToListAsync(cancellationToken);

        return administradores
            .Append(emailEmpresa ?? string.Empty)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private (string Assunto, string CorpoHtml) MontarMensagem(string razaoSocial, string cnpj, DateOnly vencimento, int diasRestantes)
    {
        var empresa = WebUtility.HtmlEncode(razaoSocial);
        var data = vencimento.ToString("dd/MM/yyyy", PtBr);

        var (assunto, situacao) = diasRestantes switch
        {
            < 0 => ($"Certificado digital VENCIDO — {razaoSocial}",
                $"O certificado digital da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>venceu em {data}</strong>. A emissão de notas está parada, inclusive a nota mensal automática."),
            0 => ($"Certificado digital vence HOJE — {razaoSocial}",
                $"O certificado digital da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>vence hoje ({data})</strong>. A partir de amanhã nenhuma nota será emitida, inclusive a nota mensal automática."),
            1 => ($"Certificado digital vence amanhã — {razaoSocial}",
                $"O certificado digital da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>vence amanhã ({data})</strong>. Depois disso nenhuma nota será emitida, inclusive a nota mensal automática."),
            _ => ($"Certificado digital vence em {diasRestantes} dias — {razaoSocial}",
                $"O certificado digital da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>vence em {data}</strong> (faltam {diasRestantes} dias). Depois do vencimento nenhuma nota será emitida, inclusive a nota mensal automática.")
        };

        var link = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Empresas";

        var corpoHtml = $"""
            <p>{situacao}</p>
            <p><strong>O que fazer:</strong> renove o certificado A1 com a sua certificadora (ou contador) e envie o arquivo novo (.pfx) no micoempresa, em <em>Empresas › Certificado</em>{(link is null ? "" : $""" (<a href="{link}">abrir Empresas</a>)""")}. Assim que o novo for enviado, os avisos param.</p>
            <p style="color:#64748b;font-size:12px">Aviso automático da plataforma micoempresa. Você recebe por ser administrador da conta ou pelo e-mail cadastrado na empresa.</p>
            """;

        return (assunto, corpoHtml);
    }
}
