using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterDocumentoContratoParaDownloadUseCase : IObterDocumentoContratoParaDownloadUseCase
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    private readonly AppDbContext _db;
    private readonly ContratoDocumentoFileStore _store;

    public ObterDocumentoContratoParaDownloadUseCase(AppDbContext db, ContratoDocumentoFileStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<DocumentoContratoParaDownloadResponse> ExecutarAsync(Guid documentoId, CancellationToken cancellationToken)
    {
        var documento = await _db.ContratoDocumentos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Documento {documentoId} não encontrado.");

        var conteudo = await _store.LerAsync(documento.TenantId, documento.EmpresaId, documento.ContratoId, documento.Id, documento.Extensao, cancellationToken);
        var contentType = ContentTypes.GetValueOrDefault(documento.Extensao, "application/octet-stream");

        return new DocumentoContratoParaDownloadResponse(documento.NomeOriginal, contentType, conteudo);
    }
}
