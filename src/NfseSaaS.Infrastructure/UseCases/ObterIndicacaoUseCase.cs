using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// "Indique e ganhe": devolve o código da conta atual e as contas que se
/// cadastraram por ele. Conta antiga (de antes da indicação) ganha o código
/// aqui, na primeira visita. As contas indicadas são de OUTROS tenants:
/// a consulta às notas delas ignora o filtro de tenant de propósito e só
/// devolve "já emitiu nota: sim/não", sem nome nem dado fiscal.
/// </summary>
public sealed class ObterIndicacaoUseCase : IObterIndicacaoUseCase
{
    private const int TentativasCodigo = 5;

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public ObterIndicacaoUseCase(AppDbContext db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    public async Task<IndicacaoResponse> ExecutarAsync(CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            throw new InvalidOperationException("Conta não identificada para a indicação.");

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == tenantId, cancellationToken);

        for (var tentativa = 1; tenant.CodigoIndicacao is null; tentativa++)
        {
            tenant.CodigoIndicacao = CodigoIndicacao.Gerar();
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (tentativa < TentativasCodigo)
            {
                // Código repetido (chance mínima) ou gravado ao mesmo tempo em outra aba: relê e tenta de novo.
                await _db.Entry(tenant).ReloadAsync(cancellationToken);
                if (tenant.CodigoIndicacao is not null)
                    break;
            }
        }

        var indicadas = await _db.Tenants.AsNoTracking()
            .Where(t => t.IndicadoPorTenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.CreatedAt, t.Ativo })
            .ToListAsync(cancellationToken);

        var ids = indicadas.Select(t => t.Id).ToList();
        var comNota = ids.Count == 0
            ? new HashSet<Guid>()
            : (await _db.NotasFiscais.IgnoreQueryFilters().AsNoTracking()
                .Where(n => ids.Contains(n.TenantId) && n.Status == NfseStatus.Autorizada)
                .Select(n => n.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();

        return new IndicacaoResponse(
            tenant.CodigoIndicacao!,
            indicadas.Select(t => new ContaIndicada(t.CreatedAt, comNota.Contains(t.Id), t.Ativo)).ToList());
    }
}
