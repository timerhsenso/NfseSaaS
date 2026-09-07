using System.Text.Json;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Auditing;

/// <summary>
/// Implementação real de IAuditLogWriter. Só adiciona ao ChangeTracker do
/// AppDbContext (nunca chama SaveChanges) — quem chamou Registrar() é
/// quem, ao final da própria unidade de trabalho, decide quando salvar.
/// TenantId é preenchido pelo AppDbContext.ApplyTenantIsolation (AuditLog
/// implementa ITenantEntity, igual toda entidade tenant-scoped).
/// </summary>
public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AuditLogWriter(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public void Registrar(string operacao, string entidade, Guid? entidadeId, object? dados = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            IpAddress = _currentUser.IpAddress,
            Operacao = operacao,
            Entidade = entidade,
            EntidadeId = entidadeId,
            Dados = dados is null ? null : JsonSerializer.Serialize(dados)
        });
    }
}
