using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// "Excluir" uma Empresa é sempre soft delete: Cliente, Servico e Nfse
/// referenciam EmpresaId, então exclusão física quebraria integridade
/// referencial e histórico fiscal. Desativar uma Empresa já inativa é
/// idempotente (não lança erro, e não gera entrada de auditoria — nada
/// mudou de fato).
/// </summary>
public sealed class DesativarEmpresaUseCase : IDesativarEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public DesativarEmpresaUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (empresa is null)
            throw new RecursoNaoEncontradoException($"Empresa {id} não encontrada.");

        if (!empresa.Ativo)
            return;

        empresa.Ativo = false;

        _auditLogWriter.Registrar("DesativarEmpresa", "Empresa", empresa.Id);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
