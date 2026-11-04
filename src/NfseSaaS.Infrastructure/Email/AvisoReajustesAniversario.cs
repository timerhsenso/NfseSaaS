using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Email;

/// <summary>
/// E-mail aos administradores da conta com os reajustes de aniversário que
/// o job diário acabou de gerar numa Empresa: os que AGUARDAM APROVAÇÃO
/// (pedem ação) e os aplicados automaticamente (só para saber). "Aguardando
/// índice" e "atrasado demais" se repetem todo dia e não geram e-mail.
/// Um e-mail por Empresa por dia (ChaveDeduplicacao), mesmo que o job rode
/// de novo. O botão "Verificar reajustes" da tela não chama isto — quem
/// clicou já vê o resultado.
/// </summary>
public sealed class AvisoReajustesAniversario
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEmailQueueService _emailQueue;
    private readonly EmailOptions _emailOptions;

    public AvisoReajustesAniversario(AppDbContext db, ICurrentTenant currentTenant, IEmailQueueService emailQueue, IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _currentTenant = currentTenant;
        _emailQueue = emailQueue;
        _emailOptions = emailOptions.Value;
    }

    /// <returns>Destinatários avisados (vazio quando não havia o que avisar ou já tinha sido avisado hoje).</returns>
    public async Task<IReadOnlyList<string>> AvisarAsync(Guid empresaId, IReadOnlyList<ReajusteAniversarioItem> itens, CancellationToken cancellationToken)
    {
        var aguardando = itens.Where(i => i.Resultado == ResultadoReajusteAniversario.AguardandoAprovacao).ToList();
        var aplicados = itens.Where(i => i.Resultado == ResultadoReajusteAniversario.AplicadoAutomaticamente).ToList();
        if (aguardando.Count == 0 && aplicados.Count == 0)
            return Array.Empty<string>();

        if (_currentTenant.TenantId is not { } tenantId)
            throw new InvalidOperationException("Tenant não definido para o aviso de reajustes.");

        var chave = $"reajustes:{empresaId:N}:{DataBrasilia.Hoje():yyyyMMdd}";
        if (await _emailQueue.JaEnfileiradoAsync(chave, cancellationToken))
            return Array.Empty<string>();

        var destinatarios = await DestinatariosAviso.AdministradoresDaContaAsync(_db, tenantId, cancellationToken);
        if (destinatarios.Count == 0)
            return destinatarios;

        var empresa = await _db.Empresas.AsNoTracking()
            .Where(e => e.Id == empresaId)
            .Select(e => e.NomeFantasia ?? e.RazaoSocial)
            .FirstOrDefaultAsync(cancellationToken) ?? "Empresa";

        var assunto = aguardando.Count > 0
            ? $"{aguardando.Count} reajuste(s) aguardando sua aprovação — {empresa}"
            : $"{aplicados.Count} reajuste(s) aplicado(s) automaticamente — {empresa}";

        var link = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Contratos";

        var corpoHtml = $"""
            <p>Reajustes de aniversário de contratos da empresa <strong>{WebUtility.HtmlEncode(empresa)}</strong> calculados hoje pelo índice de cada contrato.</p>
            {(aguardando.Count == 0 ? "" : $"""
                <p><strong>Aguardando sua aprovação</strong> — o valor novo só vale depois que você aplicar, negociar ou recusar na tela de Contratos{(link is null ? "" : $""" (<a href="{link}">abrir Contratos</a>)""")}. Enquanto isso, as notas continuam saindo com o valor atual.</p>
                {Tabela(aguardando)}
                """)}
            {(aplicados.Count == 0 ? "" : $"""
                <p><strong>Aplicados automaticamente</strong> — já valem para as próximas notas.</p>
                {Tabela(aplicados)}
                """)}
            <p style="color:#64748b;font-size:12px">Aviso automático da plataforma micoempresa para os administradores da conta.</p>
            """;

        foreach (var destinatario in destinatarios)
        {
            await _emailQueue.EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.ReajustesAniversario, null, destinatario, assunto, corpoHtml, ChaveDeduplicacao: chave),
                cancellationToken);
        }

        return destinatarios;
    }

    private static string Tabela(IEnumerable<ReajusteAniversarioItem> itens)
    {
        var linhas = string.Concat(itens.Select(i => $"""
            <tr>
                <td>{WebUtility.HtmlEncode(i.Descricao)}</td>
                <td>{i.Aniversario.ToString("dd/MM/yyyy", PtBr)}</td>
                <td>{NomeIndice(i.Indice)}</td>
                <td style="text-align:right">{(i.PercentualCalculado is { } p ? p.ToString("0.00", PtBr) + "%" : "-")}</td>
                <td style="text-align:right">{(i.ValorAnterior is { } a ? a.ToString("C", PtBr) : "-")}</td>
                <td style="text-align:right"><strong>{(i.ValorNovo is { } n ? n.ToString("C", PtBr) : "-")}</strong></td>
            </tr>
            """));

        return $"""
            <table cellpadding="6" style="border-collapse:collapse;font-size:13px" border="1">
                <tr style="background:#f1f5f9"><th>Contrato</th><th>Aniversário</th><th>Índice</th><th>Acumulado</th><th>Valor atual</th><th>Valor novo</th></tr>
                {linhas}
            </table>
            """;
    }

    private static string NomeIndice(IndiceReajusteContrato indice) => indice switch
    {
        IndiceReajusteContrato.Ipca => "IPCA",
        IndiceReajusteContrato.Igpm => "IGP-M",
        IndiceReajusteContrato.Incc => "INCC",
        IndiceReajusteContrato.Inpc => "INPC",
        IndiceReajusteContrato.Selic => "Selic",
        IndiceReajusteContrato.Cdi => "CDI",
        _ => indice.ToString()
    };
}
