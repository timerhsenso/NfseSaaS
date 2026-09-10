using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ExcluirDocumentoContratoUseCase : IExcluirDocumentoContratoUseCase
{
    private readonly AppDbContext _db;
    private readonly ContratoDocumentoFileStore _store;
    private readonly IAuditLogWriter _auditLogWriter;

    public ExcluirDocumentoContratoUseCase(AppDbContext db, ContratoDocumentoFileStore store, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _store = store;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid documentoId, CancellationToken cancellationToken)
    {
        var documento = await _db.ContratoDocumentos.FirstOrDefaultAsync(d => d.Id == documentoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Documento {documentoId} não encontrado.");

        _store.Excluir(documento.TenantId, documento.EmpresaId, documento.ContratoId, documento.Id, documento.Extensao);

        _db.ContratoDocumentos.Remove(documento);

        _auditLogWriter.Registrar("ExcluirDocumentoContrato", "Contrato", documento.ContratoId, new { documento.NomeOriginal });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
