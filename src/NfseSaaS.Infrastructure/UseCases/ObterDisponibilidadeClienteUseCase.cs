using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>Campo "Disponível para" do cliente (C10): aparece e qual é o padrão.</summary>
public sealed class ObterDisponibilidadeClienteUseCase : IObterDisponibilidadeClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public ObterDisponibilidadeClienteUseCase(AppDbContext db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    public Task<DisponibilidadeClienteResponse> ExecutarAsync(CancellationToken cancellationToken) =>
        CalcularAsync(_db, _currentTenant.TenantId, cancellationToken);

    /// <summary>Empresas ativas da conta (filtro global) e o perfil da conta.</summary>
    internal static async Task<DisponibilidadeClienteResponse> CalcularAsync(AppDbContext db, Guid? tenantId, CancellationToken cancellationToken)
    {
        var totalEmpresas = await db.Empresas.AsNoTracking().CountAsync(e => e.Ativo, cancellationToken);
        var perfil = tenantId is Guid id
            ? await db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => t.Perfil).FirstOrDefaultAsync(cancellationToken)
            : PerfilConta.Empresa;

        return new DisponibilidadeClienteResponse(
            RegrasClienteCompartilhado.MostrarCampo(totalEmpresas),
            RegrasClienteCompartilhado.PadraoCompartilhado(perfil, totalEmpresas),
            totalEmpresas);
    }
}
