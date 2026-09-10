using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>Extensões aceitas validadas aqui — Controller só valida tamanho (RequestSizeLimit), tipo de arquivo é regra de negócio.</summary>
public sealed class UploadDocumentoContratoUseCase : IUploadDocumentoContratoUseCase
{
    private static readonly HashSet<string> ExtensoesPermitidas = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx" };

    private readonly AppDbContext _db;
    private readonly ContratoDocumentoFileStore _store;
    private readonly IAuditLogWriter _auditLogWriter;

    public UploadDocumentoContratoUseCase(AppDbContext db, ContratoDocumentoFileStore store, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _store = store;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<ContratoDocumentoResponse> ExecutarAsync(Guid contratoId, string nomeOriginal, byte[] conteudo, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        var extensao = Path.GetExtension(nomeOriginal);
        if (!ExtensoesPermitidas.Contains(extensao))
            throw new RegraNegocioException("Só é permitido anexar arquivos .pdf, .doc ou .docx.");

        var documento = new ContratoDocumento
        {
            EmpresaId = contrato.EmpresaId,
            ContratoId = contratoId,
            NomeOriginal = nomeOriginal,
            Extensao = extensao,
            TamanhoBytes = conteudo.LongLength
        };

        // Grava em disco ANTES de adicionar ao ChangeTracker — se a
        // escrita em disco falhar, nada fica registrado no banco
        // apontando pra um arquivo que não existe. contrato.TenantId já
        // vem populado (é uma entidade lida do banco, não Added).
        await _store.SalvarAsync(contrato.TenantId, contrato.EmpresaId, contratoId, documento.Id, extensao, conteudo, cancellationToken);

        _db.ContratoDocumentos.Add(documento);

        _auditLogWriter.Registrar("AnexarDocumentoContrato", "Contrato", contratoId, new { documento.NomeOriginal, documento.TamanhoBytes });

        await _db.SaveChangesAsync(cancellationToken);

        return new ContratoDocumentoResponse(documento.Id, documento.ContratoId, documento.NomeOriginal, documento.Extensao, documento.TamanhoBytes, documento.CreatedAt);
    }
}
