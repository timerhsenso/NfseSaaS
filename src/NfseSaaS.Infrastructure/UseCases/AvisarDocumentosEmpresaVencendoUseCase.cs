using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// DOC1: ver IAvisarDocumentosEmpresaVencendoUseCase. Mesmo jeito do aviso
/// de certificado (AvisarCertificadoVencendoUseCase): administradores da
/// conta + e-mail da Empresa, um e-mail por destinatário com a mesma chave.
/// </summary>
public sealed class AvisarDocumentosEmpresaVencendoUseCase : IAvisarDocumentosEmpresaVencendoUseCase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEmailQueueService _emailQueue;
    private readonly EmailOptions _emailOptions;

    public AvisarDocumentosEmpresaVencendoUseCase(
        AppDbContext db, ICurrentTenant currentTenant, IEmailQueueService emailQueue, IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _currentTenant = currentTenant;
        _emailQueue = emailQueue;
        _emailOptions = emailOptions.Value;
    }

    public static string Chave(Guid documentoId, int faixa) => $"doc:{documentoId:N}:{faixa}";

    public async Task<AvisoDocumentosEmpresaResult> ExecutarAsync(Guid empresaId, DateOnly hoje, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            throw new InvalidOperationException("Tenant não definido para o aviso de documentos.");

        var empresa = await _db.Empresas.AsNoTracking()
            .Where(e => e.Id == empresaId)
            .Select(e => new { e.RazaoSocial, e.Cnpj, e.Email })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var pendentes = new List<(Domain.Entities.DocumentoEmpresa Doc, int Faixa, string Chave)>();
        foreach (var doc in await DocumentosEmpresaService.AtuaisAsync(_db, empresaId, cancellationToken))
        {
            if (RegrasDocumentoEmpresa.FaixaAviso(doc.ValidaAte, hoje) is not { } faixa)
                continue;
            var chave = Chave(doc.Id, faixa);
            if (!await _emailQueue.JaEnfileiradoAsync(chave, cancellationToken))
                pendentes.Add((doc, faixa, chave));
        }

        if (pendentes.Count == 0)
            return new AvisoDocumentosEmpresaResult(0, Array.Empty<string>(), "Nada novo para avisar.");

        var administradores = await DestinatariosAviso.AdministradoresDaContaAsync(_db, tenantId, cancellationToken);
        var destinatarios = DestinatariosAviso.Normalizar(administradores.Append(empresa.Email));
        if (destinatarios.Count == 0)
            return new AvisoDocumentosEmpresaResult(0, destinatarios, "Nenhum destinatário com e-mail.");

        foreach (var (doc, _, chave) in pendentes)
        {
            var titulo = RegrasDocumentoEmpresa.Titulo(doc.Tipo, doc.NomePersonalizado);
            var link = CatalogoDocumentosEmpresa.Obter(doc.Tipo).LinkEmissao;
            var (assunto, corpo) = MontarMensagem(titulo, link, empresa.RazaoSocial, empresa.Cnpj, doc.ValidaAte!.Value, hoje);
            foreach (var destinatario in destinatarios)
            {
                await _emailQueue.EnfileirarAsync(
                    new EmailParaEnfileirar(TipoEmailCatalogo.DocumentoVencendo, null, destinatario, assunto, corpo, ChaveDeduplicacao: chave),
                    cancellationToken);
            }
        }

        return new AvisoDocumentosEmpresaResult(pendentes.Count, destinatarios, null);
    }

    private (string Assunto, string CorpoHtml) MontarMensagem(string titulo, string? linkEmissao, string razaoSocial, string cnpj, DateOnly validaAte, DateOnly hoje)
    {
        var dias = validaAte.DayNumber - hoje.DayNumber;
        var doc = WebUtility.HtmlEncode(titulo);
        var empresa = WebUtility.HtmlEncode(razaoSocial);
        var data = validaAte.ToString("dd/MM/yyyy", PtBr);

        var (assunto, situacao) = dias switch
        {
            < 0 => ($"Documento VENCIDO: {titulo} — {razaoSocial}",
                $"O documento <strong>{doc}</strong> da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>venceu em {data}</strong>."),
            0 => ($"Documento vence HOJE: {titulo} — {razaoSocial}",
                $"O documento <strong>{doc}</strong> da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>vence hoje ({data})</strong>."),
            1 => ($"Documento vence amanhã: {titulo} — {razaoSocial}",
                $"O documento <strong>{doc}</strong> da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>vence amanhã ({data})</strong>."),
            _ => ($"Documento vence em {dias} dias: {titulo} — {razaoSocial}",
                $"O documento <strong>{doc}</strong> da empresa <strong>{empresa}</strong> (CNPJ {cnpj}) <strong>vence em {data}</strong> (faltam {dias} dias).")
        };

        var tirar = linkEmissao is null
            ? "Tire a versão nova no órgão que emite"
            : $"""Tire a versão nova no <a href="{linkEmissao}">site oficial</a>""";
        var app = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Documentos";

        var corpoHtml = $"""
            <p>{situacao}</p>
            <p><strong>O que fazer:</strong> {tirar} e envie o PDF no micoempresa, em <em>Documentos da empresa</em>{(app is null ? "" : $""" (<a href="{app}">abrir Documentos</a>)""")}. Com a versão nova enviada, os avisos deste documento param.</p>
            <p style="color:#64748b;font-size:12px">Aviso automático da plataforma micoempresa. Você recebe por ser administrador da conta ou pelo e-mail cadastrado na empresa.</p>
            """;

        return (assunto, corpoHtml);
    }
}
