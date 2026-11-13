using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Ver IEnviarNfsePorEmailUseCase. Monta o texto pelo modelo do cliente, da
/// empresa ou o padrão da conta (cadastro Modelos de e-mail) e enfileira com
/// EmailLog.NfseId — o PDF e o XML são anexados pelo
/// EmailDispatchHostedService na hora do envio (AnexosNfseEmail).
/// Responder-para: o do modelo, senão o e-mail da empresa emitente (a
/// resposta do cliente chega a quem emitiu, não se perde).
/// </summary>
public sealed class EnviarNfsePorEmailUseCase : IEnviarNfsePorEmailUseCase
{
    // Rodapé de divulgação no e-mail da nota (o tomador muitas vezes também
    // presta serviço). "origem" identifica de onde veio o cadastro.
    public static readonly string LinkDivulgacao = $"https://{NfseSaaS.Domain.Legal.DocumentosLegais.Site}/?origem=email-nota";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IModeloEmailNotaUseCase _modelos;

    public EnviarNfsePorEmailUseCase(AppDbContext db, IEmailQueueService emailQueue, IAuditLogWriter auditLogWriter, IModeloEmailNotaUseCase modelos)
    {
        _db = db;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
        _modelos = modelos;
    }

    /// <summary>O que o envio precisa saber de uma nota (também usado pela prévia e pelo lote).</summary>
    internal sealed record NotaParaEmail(
        Guid Id,
        Guid TenantId,
        Guid EmpresaId,
        Guid ClienteId,
        NfseStatus Status,
        string? Destinatario,
        bool EnvioAutomaticoLigado,
        IReadOnlyList<string> Copias,
        bool CopiasOcultas,
        DadosEmailNota Dados);

    internal static async Task<List<NotaParaEmail>> CarregarAsync(AppDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var linhas = await (
            from n in db.NotasFiscais.AsNoTracking()
            join c in db.Clientes.AsNoTracking() on n.ClienteId equals c.Id
            join e in db.Empresas.AsNoTracking() on n.EmpresaId equals e.Id
            where ids.Contains(n.Id)
            select new
            {
                n.Id, n.TenantId, n.EmpresaId, n.ClienteId, n.Status, n.NumeroNfse, n.NumeroDps, n.ChaveAcesso, n.ValorServico,
                n.DataCompetencia, n.DataEmissao, n.DescricaoServico, n.TipoAmbiente,
                NumeroContrato = db.Contratos.Where(k => k.Id == n.ContratoId).Select(k => k.Numero).FirstOrDefault(),
                ClienteNome = c.Nome,
                ClienteEmail = c.Email,
                c.EnviarNfsePorEmail,
                c.EmailsCopia,
                c.CopiaOculta,
                EmpresaNome = e.NomeFantasia ?? e.RazaoSocial,
                EmpresaRazaoSocial = e.RazaoSocial,
                EmpresaEmail = e.Email
            }).ToListAsync(cancellationToken);

        return linhas.Select(l =>
        {
            var destinatario = string.IsNullOrWhiteSpace(l.ClienteEmail) ? null : l.ClienteEmail.Trim();
            // Cópia igual ao principal não sai duas vezes (cadastro antigo/editado no banco).
            var copias = EmailsCopiaCliente.Separar(l.EmailsCopia)
                .Where(c => !string.Equals(c, destinatario, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var numero = string.IsNullOrWhiteSpace(l.NumeroNfse) ? l.NumeroDps.ToString("00000", PtBr) : l.NumeroNfse;
            var dados = new DadosEmailNota(
                l.ClienteNome, l.EmpresaNome, l.EmpresaRazaoSocial, string.IsNullOrWhiteSpace(l.EmpresaEmail) ? null : l.EmpresaEmail,
                numero, l.DataCompetencia, l.ValorServico, l.DataEmissao, l.DescricaoServico, l.NumeroContrato,
                l.ChaveAcesso, l.TipoAmbiente == TipoAmbiente.Homologacao);
            return new NotaParaEmail(l.Id, l.TenantId, l.EmpresaId, l.ClienteId, l.Status, destinatario, l.EnviarNfsePorEmail, copias, l.CopiaOculta, dados);
        }).ToList();
    }

    public async Task<EnviarNfsePorEmailResult> ExecutarAsync(Guid nfseId, bool automatico, TextoEmailNota? texto, CancellationToken cancellationToken)
    {
        var nota = (await CarregarAsync(_db, new[] { nfseId }, cancellationToken)).FirstOrDefault()
            ?? throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        if (nota.Status != NfseStatus.Autorizada)
        {
            if (automatico)
                return new EnviarNfsePorEmailResult(false, null, "Nota não autorizada.");
            throw new RegraNegocioException("Só é possível enviar por e-mail uma nota autorizada.");
        }

        if (automatico && !nota.EnvioAutomaticoLigado)
            return new EnviarNfsePorEmailResult(false, null, "Envio automático desligado no cadastro do cliente.");

        if (nota.Destinatario is not { } destinatario)
        {
            if (automatico)
                return new EnviarNfsePorEmailResult(false, null, "Cliente sem e-mail cadastrado.");
            throw new RegraNegocioException("O cliente desta nota não tem e-mail cadastrado. Inclua o e-mail no cadastro do cliente e tente de novo.");
        }

        var modelo = await _modelos.ResolverAsync(nota.TenantId, nota.EmpresaId, nota.ClienteId, texto?.ModeloId, cancellationToken);
        var assuntoModelo = string.IsNullOrWhiteSpace(texto?.Assunto) ? modelo.Assunto : texto.Assunto;
        var mensagemModelo = string.IsNullOrWhiteSpace(texto?.Mensagem) ? modelo.Mensagem : texto.Mensagem;
        if (texto is not null)
        {
            var erros = RegrasModeloEmailNota.Validar(assuntoModelo, mensagemModelo, null, null);
            if (erros.Count > 0)
                throw new RegraNegocioException(string.Join(" ", erros));
        }

        var assunto = RegrasModeloEmailNota.MontarAssunto(assuntoModelo, nota.Dados);
        var corpoHtml = RegrasModeloEmailNota.MontarCorpoHtml(mensagemModelo, nota.Dados, !modelo.OcultarDivulgacao, LinkDivulgacao);
        var responderPara = modelo.ResponderPara ?? nota.Dados.EmpresaEmail;

        var emailLogId = await _emailQueue.EnfileirarAsync(
            new EmailParaEnfileirar(TipoEmailCatalogo.NfseTomador, null, destinatario, assunto, corpoHtml, NfseId: nfseId,
                ResponderPara: responderPara, Copias: nota.Copias, CopiasOcultas: nota.CopiasOcultas,
                CopiaOcultaFixa: modelo.CopiaOcultaPara),
            cancellationToken);

        _auditLogWriter.Registrar("EnviarNfsePorEmail", "Nfse", nfseId,
            new { destinatario, copias = nota.Copias, copiasOcultas = nota.CopiasOcultas, copiaOcultaFixa = modelo.CopiaOcultaPara, modelo = modelo.Nome, automatico, textoProprio = texto is not null, emailLogId });
        await _db.SaveChangesAsync(cancellationToken);

        return new EnviarNfsePorEmailResult(true, destinatario, null, nota.Copias);
    }
}
