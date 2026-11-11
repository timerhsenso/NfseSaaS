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
/// devolve "já emitiu nota: sim/não", sem nome nem dado fiscal, e a
/// situação do prêmio que a conta atual ganhou por cada uma.
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
                .Where(n => ids.Contains(n.TenantId) && n.Status == NfseStatus.Autorizada && n.TipoAmbiente == TipoAmbiente.Producao)
                .Select(n => n.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();

        var premios = ids.Count == 0
            ? new Dictionary<Guid, (StatusPremioIndicacao Status, DateTimeOffset? AplicadoEm)>()
            : (await _db.PremiosIndicacao.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.Papel == PapelPremioIndicacao.QuemIndicou && ids.Contains(p.IndicadoTenantId))
                .Select(p => new { p.IndicadoTenantId, p.Status, p.AplicadoEm })
                .ToListAsync(cancellationToken))
                .ToDictionary(p => p.IndicadoTenantId, p => (p.Status, p.AplicadoEm));

        var desde = DateTimeOffset.UtcNow.AddDays(-RegrasConviteIndicacao.IntervaloMesmoEmailDias);
        var convites = await _db.ConvitesIndicacao.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CreatedAt >= desde)
            .OrderByDescending(c => c.CreatedAt)
            .Take(100)
            .Select(c => new ConviteEnviado(c.Email, c.CreatedAt))
            .ToListAsync(cancellationToken);
        var restamHoje = await ConvidarPorEmailUseCase.RestamHojeAsync(_db, tenantId, cancellationToken);

        return new IndicacaoResponse(
            tenant.CodigoIndicacao!,
            indicadas.Select(t =>
            {
                var temPremio = premios.TryGetValue(t.Id, out var premio);
                return new ContaIndicada(t.CreatedAt, comNota.Contains(t.Id), t.Ativo,
                    temPremio ? premio.Status : null, temPremio ? premio.AplicadoEm : null);
            }).ToList(),
            convites,
            restamHoje);
    }
}
