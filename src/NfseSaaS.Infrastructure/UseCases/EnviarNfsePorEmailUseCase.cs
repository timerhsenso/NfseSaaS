using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
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

    private readonly NfseSaaS.Application.UseCases.Clientes.ILinkSaidaCopia _linkSaidaCopia;
    private readonly ILinkConfirmacaoRecebimento _linkConfirmacao;

    public EnviarNfsePorEmailUseCase(AppDbContext db, IEmailQueueService emailQueue, IAuditLogWriter auditLogWriter, IModeloEmailNotaUseCase modelos,
        NfseSaaS.Application.UseCases.Clientes.ILinkSaidaCopia linkSaidaCopia, ILinkConfirmacaoRecebimento linkConfirmacao)
    {
        _linkConfirmacao = linkConfirmacao;
        _db = db;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
        _modelos = modelos;
        _linkSaidaCopia = linkSaidaCopia;
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
        DadosEmailNota Dados,
        IReadOnlyList<NfseSaaS.Application.UseCases.DocumentosEmpresa.ChaveDocumento> DocumentosExigidos,
        AcaoDocumentoPendente AcaoDocumentoPendente);

    internal static async Task<List<NotaParaEmail>> CarregarAsync(AppDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var linhas = await (
            from n in db.NotasFiscais.AsNoTracking()
            join c in db.Clientes.AsNoTracking() on n.ClienteId equals c.Id
            join e in db.Empresas.AsNoTracking() on n.EmpresaId equals e.Id
            where ids.Contains(n.Id)
            select new
            {
                n.Id, n.TenantId, n.EmpresaId, n.ClienteId, n.Status, n.NumeroNfse, n.NumeroDps, n.ChaveAcesso, n.ValorServico, n.ValorLiquido,
                n.DataCompetencia, n.DataEmissao, n.DescricaoServico, n.TipoAmbiente,
                NumeroContrato = db.Contratos.Where(k => k.Id == n.ContratoId).Select(k => k.Numero).FirstOrDefault(),
                ClienteNome = c.Nome,
                ClienteEmail = c.Email,
                c.EnviarNfsePorEmail,
                c.EmailsCopia,
                c.CopiaOculta,
                c.DocumentosExigidos,
                c.DocumentoPendenteEnvioAutomatico,
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
                l.ChaveAcesso, l.TipoAmbiente == TipoAmbiente.Homologacao, l.ValorLiquido);
            return new NotaParaEmail(l.Id, l.TenantId, l.EmpresaId, l.ClienteId, l.Status, destinatario, l.EnviarNfsePorEmail, copias, l.CopiaOculta, dados,
                RegrasDocumentosExigidos.Ler(l.DocumentosExigidos), l.DocumentoPendenteEnvioAutomatico);
        }).ToList();
    }

    /// <summary>DOC2: os documentos exigidos pelo cliente, avaliados contra a versão que vale na empresa da nota.</summary>
    internal static async Task<Dictionary<Guid, IReadOnlyList<DocumentoExigidoAvaliado>>> AvaliarDocumentosAsync(
        AppDbContext db, IReadOnlyCollection<NotaParaEmail> notas, CancellationToken cancellationToken)
    {
        var comExigidos = notas.Where(n => n.DocumentosExigidos.Count > 0).ToList();
        if (comExigidos.Count == 0)
            return notas.ToDictionary(n => n.Id, _ => (IReadOnlyList<DocumentoExigidoAvaliado>)Array.Empty<DocumentoExigidoAvaliado>());

        var catalogo = await new CatalogoDocumentosService(db).ObterAsync(cancellationToken);
        var atuais = await DocumentosEmpresaService.AtuaisParaEmailAsync(db, comExigidos.Select(n => n.EmpresaId).Distinct().ToList(), catalogo, cancellationToken);
        var hoje = DataBrasilia.Hoje();
        return notas.ToDictionary(n => n.Id, n => n.DocumentosExigidos.Count == 0
            ? (IReadOnlyList<DocumentoExigidoAvaliado>)Array.Empty<DocumentoExigidoAvaliado>()
            : RegrasDocumentosExigidos.Avaliar(n.DocumentosExigidos, atuais.GetValueOrDefault(n.EmpresaId) ?? new List<DocumentoAtual>(), hoje, catalogo));
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

        // DOC2: documentos exigidos pelo cliente. Automático: vale a escolha do
        // cadastro (pode segurar o e-mail); pela tela: a escolha do usuário.
        var documentos = (await AvaliarDocumentosAsync(_db, new[] { nota }, cancellationToken))[nota.Id];
        var decisao = automatico
            ? RegrasDocumentosExigidos.DecidirAutomatico(documentos, nota.AcaoDocumentoPendente)
            : RegrasDocumentosExigidos.DecidirManual(documentos, texto?.DocumentosIds, texto?.EnviarVencidos ?? false);
        if (decisao.SegurarEmail)
        {
            _auditLogWriter.Registrar("EmailNotaSeguradoPorDocumento", "Nfse", nfseId, new { destinatario, alerta = decisao.Alerta });
            await _db.SaveChangesAsync(cancellationToken);
            return new EnviarNfsePorEmailResult(false, destinatario, decisao.Alerta, AlertaDocumentos: decisao.Alerta);
        }

        var modelo = await _modelos.ResolverAsync(nota.TenantId, nota.EmpresaId, nota.ClienteId, texto?.ModeloId, cancellationToken);
        var assuntoModelo = string.IsNullOrWhiteSpace(texto?.Assunto) ? modelo.Assunto : texto.Assunto;
        var mensagemModelo = string.IsNullOrWhiteSpace(texto?.Mensagem) ? modelo.Mensagem : texto.Mensagem;
        if (!string.IsNullOrWhiteSpace(texto?.Assunto) || !string.IsNullOrWhiteSpace(texto?.Mensagem))
        {
            var erros = RegrasModeloEmailNota.Validar(assuntoModelo, mensagemModelo, null, null);
            if (erros.Count > 0)
                throw new RegraNegocioException(string.Join(" ", erros));
        }

        var assunto = RegrasModeloEmailNota.MontarAssunto(assuntoModelo, nota.Dados);
        // RC1: com cópia (e o modelo não tirou a linha), o rodapé leva o "pedir para sair das cópias".
        var linkSaida = nota.Copias.Count > 0 && !modelo.OcultarSaidaCopias ? _linkSaidaCopia.Gerar(nota.TenantId, nfseId) : null;
        // CR1: o campo [Confirmar recebimento] vira botão/link com a validade do modelo.
        var linkConfirmacao = RegrasModeloEmailNota.PedeConfirmacao(mensagemModelo)
            ? _linkConfirmacao.Gerar(nota.TenantId, nfseId, modelo.ValidadeConfirmacaoDias)
            : null;
        var corpoHtml = RegrasModeloEmailNota.MontarCorpoHtml(mensagemModelo, nota.Dados, !modelo.OcultarDivulgacao, LinkDivulgacao, linkSaida, linkConfirmacao);
        var responderPara = modelo.ResponderPara ?? nota.Dados.EmpresaEmail;

        var emailLogId = await _emailQueue.EnfileirarAsync(
            new EmailParaEnfileirar(TipoEmailCatalogo.NfseTomador, null, destinatario, assunto, corpoHtml, NfseId: nfseId,
                ResponderPara: responderPara, Copias: nota.Copias, CopiasOcultas: nota.CopiasOcultas,
                CopiaOcultaFixa: modelo.CopiaOcultaPara,
                DocumentosAnexos: RegrasDocumentosExigidos.Serializar(decisao.Anexos),
                AlertaDocumentos: decisao.Alerta),
            cancellationToken);

        _auditLogWriter.Registrar("EnviarNfsePorEmail", "Nfse", nfseId,
            new { destinatario, copias = nota.Copias, copiasOcultas = nota.CopiasOcultas, copiaOcultaFixa = modelo.CopiaOcultaPara, modelo = modelo.Nome, automatico,
                textoProprio = !string.IsNullOrWhiteSpace(texto?.Assunto) || !string.IsNullOrWhiteSpace(texto?.Mensagem),
                pedeConfirmacao = linkConfirmacao is not null,
                documentos = decisao.Anexos.Select(a => a.Titulo).ToList(), alertaDocumentos = decisao.Alerta, emailLogId });
        await _db.SaveChangesAsync(cancellationToken);

        return new EnviarNfsePorEmailResult(true, destinatario, null, nota.Copias, decisao.Alerta);
    }
}
