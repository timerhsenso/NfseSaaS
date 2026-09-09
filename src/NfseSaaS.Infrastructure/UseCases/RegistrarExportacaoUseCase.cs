using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Exportacoes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class RegistrarExportacaoUseCase : IRegistrarExportacaoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public RegistrarExportacaoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(string tela, string formato, CancellationToken cancellationToken)
    {
        _auditLogWriter.Registrar("ExportarGrid", tela, entidadeId: null, dados: new { formato });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
