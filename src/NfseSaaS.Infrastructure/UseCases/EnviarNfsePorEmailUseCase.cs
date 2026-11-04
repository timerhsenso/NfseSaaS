using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Ver IEnviarNfsePorEmailUseCase. Só monta o texto e enfileira com
/// EmailLog.NfseId — o PDF e o XML são anexados pelo
/// EmailDispatchHostedService na hora do envio (AnexosNfseEmail).
/// </summary>
public sealed class EnviarNfsePorEmailUseCase : IEnviarNfsePorEmailUseCase
{
    // Rodapé de divulgação no e-mail da nota (o tomador muitas vezes também
    // presta serviço). "origem" identifica de onde veio o cadastro.
    private static readonly string LinkDivulgacao = $"https://{NfseSaaS.Domain.Legal.DocumentosLegais.Site}/?origem=email-nota";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan OffsetBrasilia = TimeSpan.FromHours(-3);

    private readonly AppDbContext _db;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;

    public EnviarNfsePorEmailUseCase(AppDbContext db, IEmailQueueService emailQueue, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<EnviarNfsePorEmailResult> ExecutarAsync(Guid nfseId, bool automatico, CancellationToken cancellationToken)
    {
        var dados = await (
            from n in _db.NotasFiscais.AsNoTracking()
            join c in _db.Clientes.AsNoTracking() on n.ClienteId equals c.Id
            join e in _db.Empresas.AsNoTracking() on n.EmpresaId equals e.Id
            where n.Id == nfseId
            select new
            {
                n.Status,
                n.NumeroNfse,
                n.NumeroDps,
                n.ChaveAcesso,
                n.ValorServico,
                n.DataCompetencia,
                n.DataEmissao,
                n.DescricaoServico,
                n.TipoAmbiente,
                ClienteNome = c.Nome,
                ClienteEmail = c.Email,
                c.NaoEnviarNfsePorEmail,
                EmpresaNome = e.NomeFantasia ?? e.RazaoSocial,
                EmpresaRazaoSocial = e.RazaoSocial,
                EmpresaEmail = e.Email
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        if (dados.Status != NfseStatus.Autorizada)
        {
            if (automatico)
                return new EnviarNfsePorEmailResult(false, null, "Nota não autorizada.");
            throw new RegraNegocioException("Só é possível enviar por e-mail uma nota autorizada.");
        }

        if (automatico && dados.NaoEnviarNfsePorEmail)
            return new EnviarNfsePorEmailResult(false, null, "Envio automático desligado no cadastro do cliente.");

        var destinatario = dados.ClienteEmail?.Trim();
        if (string.IsNullOrWhiteSpace(destinatario))
        {
            if (automatico)
                return new EnviarNfsePorEmailResult(false, null, "Cliente sem e-mail cadastrado.");
            throw new RegraNegocioException("O cliente desta nota não tem e-mail cadastrado. Inclua o e-mail no cadastro do cliente e tente de novo.");
        }

        var numero = string.IsNullOrWhiteSpace(dados.NumeroNfse) ? dados.NumeroDps.ToString("00000", PtBr) : dados.NumeroNfse;
        var homologacao = dados.TipoAmbiente == TipoAmbiente.Homologacao;
        var dataEmissao = dados.DataEmissao?.ToOffset(OffsetBrasilia).ToString("dd/MM/yyyy", PtBr) ?? "-";
        var linkConsulta = string.IsNullOrWhiteSpace(dados.ChaveAcesso)
            ? null
            : $"https://www.nfse.gov.br/ConsultaPublica/?tpc=1&chave={Uri.EscapeDataString(dados.ChaveAcesso)}";

        var assunto = $"{(homologacao ? "[TESTE] " : "")}Nota fiscal de serviço — {dados.EmpresaNome}";

        var corpoHtml = $"""
            <p>Olá, {WebUtility.HtmlEncode(dados.ClienteNome)}.</p>
            <p><strong>{WebUtility.HtmlEncode(dados.EmpresaRazaoSocial)}</strong> emitiu uma nota fiscal de serviço eletrônica (NFS-e) para você. O PDF (DANFSe) e o XML estão anexados a este e-mail.</p>
            {(homologacao ? "<p><strong>Atenção: esta nota foi emitida no ambiente de TESTES da Receita e não tem valor fiscal.</strong></p>" : "")}
            <table cellpadding="4" style="border-collapse:collapse">
                <tr><td>Número</td><td><strong>{WebUtility.HtmlEncode(numero)}</strong></td></tr>
                <tr><td>Emissão</td><td>{dataEmissao}</td></tr>
                <tr><td>Competência</td><td>{dados.DataCompetencia.ToString("MM/yyyy", PtBr)}</td></tr>
                <tr><td>Valor do serviço</td><td>{dados.ValorServico.ToString("C", PtBr)}</td></tr>
                <tr><td>Serviço</td><td>{WebUtility.HtmlEncode(Resumir(dados.DescricaoServico, 300))}</td></tr>
                {(dados.ChaveAcesso is null ? "" : $"<tr><td>Chave de acesso</td><td>{WebUtility.HtmlEncode(dados.ChaveAcesso)}</td></tr>")}
            </table>
            {(linkConsulta is null ? "" : $"""<p>Você pode conferir a autenticidade da nota na <a href="{linkConsulta}">consulta pública da NFS-e</a>.</p>""")}
            <p>Dúvidas sobre a nota? Fale com {WebUtility.HtmlEncode(dados.EmpresaNome)}{(string.IsNullOrWhiteSpace(dados.EmpresaEmail) ? "" : $" pelo e-mail {WebUtility.HtmlEncode(dados.EmpresaEmail)}")}.</p>
            <p style="color:#64748b;font-size:12px">Enviado pela plataforma micoempresa em nome do emissor. Não responda a este e-mail.</p>
            <p style="color:#64748b;font-size:12px">Você também presta serviço? Emita suas notas com o <a href="{LinkDivulgacao}" style="color:#2563eb">micoempresa</a>: nota mensal automática e nota enviada por e-mail ao seu cliente. Crie sua conta grátis.</p>
            """;

        var emailLogId = await _emailQueue.EnfileirarAsync(
            new EmailParaEnfileirar(TipoEmailCatalogo.NfseTomador, null, destinatario, assunto, corpoHtml, NfseId: nfseId),
            cancellationToken);

        _auditLogWriter.Registrar("EnviarNfsePorEmail", "Nfse", nfseId, new { destinatario, automatico, emailLogId });
        await _db.SaveChangesAsync(cancellationToken);

        return new EnviarNfsePorEmailResult(true, destinatario, null);
    }

    private static string Resumir(string texto, int maximo) =>
        texto.Length <= maximo ? texto : texto[..maximo] + "…";
}
