using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Modal de envio (texto já preenchido, ajustável) e envio de várias notas
/// de uma vez pela barra de seleção da lista de Notas. Um e-mail por nota,
/// cada um com o modelo da sua empresa. "Já enviada" = tem e-mail da nota
/// enviado ou na fila (falha não conta: pode mandar de novo).
/// </summary>
public sealed class EnvioEmailNfseService : IEnvioEmailNfseUseCase
{
    private readonly AppDbContext _db;
    private readonly IEnviarNfsePorEmailUseCase _enviar;
    private readonly IModeloEmailNotaUseCase _modelos;

    public EnvioEmailNfseService(AppDbContext db, IEnviarNfsePorEmailUseCase enviar, IModeloEmailNotaUseCase modelos)
    {
        _db = db;
        _enviar = enviar;
        _modelos = modelos;
    }

    public async Task<PreviaEmailNfseResponse> PreviaAsync(Guid nfseId, Guid? modeloId, CancellationToken cancellationToken)
    {
        var nota = (await EnviarNfsePorEmailUseCase.CarregarAsync(_db, new[] { nfseId }, cancellationToken)).FirstOrDefault()
            ?? throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        var modelo = await _modelos.ResolverAsync(nota.TenantId, nota.EmpresaId, nota.ClienteId, modeloId, cancellationToken);
        var valores = RegrasModeloEmailNota.Valores(nota.Dados);
        var ultimo = await _db.EmailLogs.AsNoTracking()
            .Where(e => e.NfseId == nfseId && e.Tipo == TipoEmailCatalogo.NfseTomador)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new { e.CreatedAt, e.EnviadoEm, e.Status })
            .FirstOrDefaultAsync(cancellationToken);

        var documentos = (await EnviarNfsePorEmailUseCase.AvaliarDocumentosAsync(_db, new[] { nota }, cancellationToken))[nota.Id]
            .Select(d => new DocumentoEmailPrevia(d.Documento?.Id, d.Titulo, d.Situacao, RegrasDocumentosExigidos.TextoSituacao(d),
                d.Documento?.NomeArquivo, d.Documento?.TamanhoBytes ?? 0))
            .ToList();

        return new PreviaEmailNfseResponse(
            nfseId,
            modelo.ModeloId,
            await _modelos.OpcoesAsync(cancellationToken),
            nota.Destinatario,
            nota.Copias,
            nota.CopiasOcultas,
            modelo.CopiaOcultaPara,
            modelo.ResponderPara ?? nota.Dados.EmpresaEmail,
            RegrasModeloEmailNota.Preencher(modelo.Assunto, valores),
            RegrasModeloEmailNota.Preencher(modelo.Mensagem, valores),
            nota.Dados.Homologacao,
            ultimo is null ? null : ultimo.EnviadoEm ?? ultimo.CreatedAt,
            ultimo?.Status,
            Impedimento(nota),
            documentos);
    }

    public async Task<PreviaEnvioEmailLoteResponse> PreviaLoteAsync(EnvioEmailLoteRequest request, CancellationToken cancellationToken)
    {
        var (notas, jaEnviadas) = await CarregarLoteAsync(request, cancellationToken);
        var enviaveis = notas.Where(n => Impedimento(n) is null).ToList();
        var documentos = await EnviarNfsePorEmailUseCase.AvaliarDocumentosAsync(_db, enviaveis, cancellationToken);

        return new PreviaEnvioEmailLoteResponse(
            notas.Count,
            enviaveis.Count,
            enviaveis.Count(n => jaEnviadas.Contains(n.Id)),
            notas.Count(n => n.Status == NfseStatus.Autorizada && n.Destinatario is null),
            notas.Count(n => n.Status != NfseStatus.Autorizada),
            enviaveis.Select(n => n.ClienteId).Distinct().Count(),
            enviaveis.Count(n => documentos[n.Id].Any(d => d.Situacao == SituacaoDocumentoExigido.Vencido)),
            enviaveis.Count(n => documentos[n.Id].Any(d => d.Situacao == SituacaoDocumentoExigido.Faltando)));
    }

    public async Task<ResultadoEnvioEmailLoteResponse> EnviarLoteAsync(EnvioEmailLoteRequest request, CancellationToken cancellationToken)
    {
        var (notas, jaEnviadas) = await CarregarLoteAsync(request, cancellationToken);

        var enfileiradas = 0;
        var puladas = 0;
        var naoEnviadas = new List<NotaNaoEnviadaPorEmail>();

        // Ordem da tela (a do pedido), não a do banco.
        foreach (var nota in request.NfseIds.Distinct().Select(id => notas.FirstOrDefault(n => n.Id == id)).OfType<EnviarNfsePorEmailUseCase.NotaParaEmail>())
        {
            if (Impedimento(nota) is { } motivo)
            {
                naoEnviadas.Add(new NotaNaoEnviadaPorEmail(nota.Id, nota.Dados.Numero, nota.Dados.Cliente, motivo));
                continue;
            }

            if (request.PularJaEnviadas && jaEnviadas.Contains(nota.Id))
            {
                puladas++;
                continue;
            }

            try
            {
                // Sem marcar documento um a um: os em dia vão; o vencido só com a resposta do lote.
                await _enviar.ExecutarAsync(nota.Id, automatico: false,
                    new TextoEmailNota(null, null, EnviarVencidos: request.EnviarDocumentosVencidos), cancellationToken);
                enfileiradas++;
            }
            catch (RegraNegocioException ex)
            {
                naoEnviadas.Add(new NotaNaoEnviadaPorEmail(nota.Id, nota.Dados.Numero, nota.Dados.Cliente, ex.Message));
            }
        }

        return new ResultadoEnvioEmailLoteResponse(enfileiradas, puladas, naoEnviadas);
    }

    private async Task<(List<EnviarNfsePorEmailUseCase.NotaParaEmail> Notas, HashSet<Guid> JaEnviadas)> CarregarLoteAsync(
        EnvioEmailLoteRequest request, CancellationToken cancellationToken)
    {
        var ids = (request.NfseIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (ids.Count == 0)
            throw new RegraNegocioException("Selecione ao menos uma nota.");
        if (ids.Count > IEnvioEmailNfseUseCase.MaximoPorLote)
            throw new RegraNegocioException($"Envie no máximo {IEnvioEmailNfseUseCase.MaximoPorLote} notas por vez.");

        var notas = await EnviarNfsePorEmailUseCase.CarregarAsync(_db, ids, cancellationToken);
        var jaEnviadas = (await _db.EmailLogs.AsNoTracking()
                .Where(e => e.NfseId != null && ids.Contains(e.NfseId.Value)
                    && e.Tipo == TipoEmailCatalogo.NfseTomador && e.Status != StatusEmailLog.Falhou)
                .Select(e => e.NfseId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return (notas, jaEnviadas);
    }

    private static string? Impedimento(EnviarNfsePorEmailUseCase.NotaParaEmail nota) =>
        nota.Status != NfseStatus.Autorizada ? "Nota não autorizada."
        : nota.Destinatario is null ? "Cliente sem e-mail no cadastro."
        : null;
}
