using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Auditing;

/// <summary>
/// Implementação real de INfseEventoWriter. Só adiciona ao ChangeTracker
/// do AppDbContext (nunca chama SaveChanges) — quem chamou Registrar() é
/// quem, ao final da própria unidade de trabalho, decide quando salvar.
/// TenantId é preenchido pelo AppDbContext.ApplyTenantIsolation
/// (NfseEvento implementa ITenantEntity, igual toda entidade
/// tenant-scoped).
/// </summary>
public sealed class NfseEventoWriter : INfseEventoWriter
{
    private readonly AppDbContext _db;

    public NfseEventoWriter(AppDbContext db)
    {
        _db = db;
    }

    public void Registrar(Guid nfseId, NfseEventoTipo tipo, string? codigo = null, string? mensagem = null)
    {
        _db.NfseEventos.Add(new NfseEvento
        {
            NfseId = nfseId,
            Tipo = tipo,
            Codigo = codigo,
            Mensagem = mensagem
        });
    }
}
