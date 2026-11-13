using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Email;

/// <summary>
/// Monta os anexos do e-mail da nota ao tomador — PDF (DANFSe) e XML — a
/// partir da própria Nfse, na hora do envio (ver EmailLog.NfseId). Precisa
/// do Tenant da nota já definido no escopo (o EmailDispatchHostedService
/// faz isso), porque a leitura passa pelo isolamento normal.
///
/// O XML é obrigatório (é o documento fiscal); sem ele o envio falha e fica
/// registrado. O PDF é "melhor esforço": se a geração falhar, o e-mail sai
/// só com o XML e o motivo vai pro log — o tomador ainda recebe a nota.
/// </summary>
public sealed class AnexosNfseEmail
{
    private readonly AppDbContext _db;
    private readonly IObterDanfsePdfUseCase _obterDanfsePdf;
    private readonly ILogger<AnexosNfseEmail> _logger;
    private readonly DocumentoEmpresaFileStore _documentos;

    public AnexosNfseEmail(AppDbContext db, IObterDanfsePdfUseCase obterDanfsePdf, ILogger<AnexosNfseEmail> logger, DocumentoEmpresaFileStore documentos)
    {
        _db = db;
        _obterDanfsePdf = obterDanfsePdf;
        _logger = logger;
        _documentos = documentos;
    }

    /// <param name="documentosAnexos">DOC2: EmailLog.DocumentosAnexos — documentos da empresa que o cliente exige (versões fixas).</param>
    public async Task<IReadOnlyCollection<AnexoEmail>> MontarAsync(Guid nfseId, string? documentosAnexos, CancellationToken cancellationToken)
    {
        var anexos = (await MontarNotaAsync(nfseId, cancellationToken)).ToList();
        var nomes = anexos.Select(a => a.NomeArquivo).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Documento que sumiu do disco (ou foi excluído) não derruba o e-mail da
        // nota: vai sem ele e fica no log do servidor.
        foreach (var doc in RegrasDocumentosExigidos.Desserializar(documentosAnexos))
        {
            var registro = await _db.DocumentosEmpresa.AsNoTracking()
                .Where(d => d.Id == doc.Id)
                .Select(d => new { d.TenantId, d.EmpresaId })
                .FirstOrDefaultAsync(cancellationToken);
            if (registro is null)
            {
                _logger.LogWarning("Documento {DocumentoId} ({Titulo}) não existe mais; e-mail da nota {NfseId} vai sem ele.", doc.Id, doc.Titulo, nfseId);
                continue;
            }

            try
            {
                var bytes = await _documentos.LerAsync(registro.TenantId, registro.EmpresaId, doc.Id, cancellationToken);
                var nome = RegrasDocumentoEmpresa.NomeArquivoSeguro(doc.Titulo);
                for (var n = 2; !nomes.Add(nome); n++)
                    nome = RegrasDocumentoEmpresa.NomeArquivoSeguro($"{doc.Titulo} {n}");
                anexos.Add(new AnexoEmail(nome, "application/pdf", bytes));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Arquivo do documento {DocumentoId} ({Titulo}) não pôde ser lido; e-mail da nota {NfseId} vai sem ele.", doc.Id, doc.Titulo, nfseId);
            }
        }

        return anexos;
    }

    private async Task<IReadOnlyCollection<AnexoEmail>> MontarNotaAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        var nfse = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.Id == nfseId)
            .Select(n => new { n.XmlNfse, n.NumeroDps })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Nota {nfseId} não encontrada para anexar ao e-mail.");

        if (string.IsNullOrWhiteSpace(nfse.XmlNfse))
            throw new InvalidOperationException("A nota não tem o XML autorizado guardado — não há o que anexar.");

        var anexos = new List<AnexoEmail>();
        var nomeBase = $"NFSe_{nfse.NumeroDps:00000}";

        try
        {
            var pdf = await _obterDanfsePdf.ExecutarAsync(nfseId, cancellationToken);
            anexos.Add(new AnexoEmail(pdf.NomeArquivo, pdf.ContentType, pdf.Bytes));
            nomeBase = Path.GetFileNameWithoutExtension(pdf.NomeArquivo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PDF da nota {NfseId} não pôde ser gerado; o e-mail vai só com o XML.", nfseId);
        }

        anexos.Add(new AnexoEmail($"{nomeBase}.xml", "application/xml", Encoding.UTF8.GetBytes(nfse.XmlNfse)));

        return anexos;
    }
}
