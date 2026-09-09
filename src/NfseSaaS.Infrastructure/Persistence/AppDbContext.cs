using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Infrastructure.Persistence;

/// <summary>
/// DbContext principal do SaaS. Aplica isolamento de multi-tenant através
/// de Global Query Filters: toda entidade que implementa ITenantEntity é
/// automaticamente filtrada por TenantId == ICurrentTenant.TenantId em
/// TODAS as consultas — não é necessário (nem permitido) que os
/// desenvolvedores escrevam .Where(x => x.TenantId == tenantId) à mão.
///
/// Entidades GLOBAIS (não implementam ITenantEntity) — hoje apenas
/// <see cref="Tenant"/> — NÃO sofrem esse filtro.
/// </summary>
public sealed class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    private readonly ICurrentTenant _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<Nfse> NotasFiscais => Set<Nfse>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ContadorDps> ContadoresDps => Set<ContadorDps>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Aplica, via reflexão, um Global Query Filter (e => e.TenantId ==
    /// _currentTenant.TenantId) em toda entidade que implementa
    /// ITenantEntity. Centralizado aqui para que nenhuma consulta no
    /// restante da aplicação precise (ou consiga esquecer de) filtrar por
    /// tenant manualmente.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");

            var tenantIdProperty = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
            var currentTenantId = Expression.Property(
                Expression.Constant(_currentTenant),
                nameof(ICurrentTenant.TenantId));

            // e.TenantId == (_currentTenant.TenantId ?? Guid.Empty)
            // Quando não há tenant resolvido, o filtro força um resultado
            // vazio (Guid.Empty não corresponde a nenhum registro real) em
            // vez de expor todos os dados de todos os tenants.
            var currentTenantIdValue = Expression.Property(currentTenantId, nameof(Nullable<Guid>.Value));
            var hasValue = Expression.Property(currentTenantId, nameof(Nullable<Guid>.HasValue));

            var comparison = Expression.AndAlso(
                hasValue,
                Expression.Equal(tenantIdProperty, currentTenantIdValue));

            var lambda = Expression.Lambda(comparison, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantIsolation();
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantIsolation();
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Isolamento de multi-tenant nas ESCRITAS — complementa o Global Query
    /// Filter (que só protege leituras). Sem isto, nada impediria um bug
    /// (ou uma requisição manipulada) de gravar uma entidade tenant-scoped
    /// com o TenantId de outro tenant. Regras:
    ///
    /// - Added: o TenantId é SEMPRE sobrescrito pelo tenant atual — nunca se
    ///   confia em um valor de TenantId vindo de fora da camada de
    ///   persistência, mesmo que ele já "pareça" correto.
    /// - Modified/Deleted: se a entidade em memória não pertence ao tenant
    ///   atual, a operação é rejeitada (lança exceção) — isto só pode
    ///   acontecer se alguém montar/anexar a entidade manualmente, já que o
    ///   Global Query Filter garante que toda entidade CARREGADA por consulta
    ///   já pertence ao tenant atual.
    /// - Modified: uma tentativa de alterar o próprio TenantId é sempre
    ///   revertida antes de persistir — TenantId é imutável após a criação.
    /// - Sem tenant resolvido (ex.: contexto de sistema/seed): qualquer
    ///   gravação de entidade tenant-scoped é rejeitada.
    /// </summary>
    private void ApplyTenantIsolation()
    {
        var tenantId = _currentTenant.TenantId;

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (tenantId is null)
                        throw new InvalidOperationException(
                            $"Não é possível gravar '{entry.Entity.GetType().Name}': nenhum Tenant resolvido para a requisição atual.");

                    entry.Entity.TenantId = tenantId.Value;
                    break;

                case EntityState.Modified:
                    if (tenantId is null || entry.Entity.TenantId != tenantId.Value)
                        throw new InvalidOperationException(
                            $"Tentativa de modificar '{entry.Entity.GetType().Name}' fora do Tenant atual.");

                    // TenantId é imutável após a criação — qualquer alteração é descartada.
                    entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;
                    break;

                case EntityState.Deleted:
                    if (tenantId is null || entry.Entity.TenantId != tenantId.Value)
                        throw new InvalidOperationException(
                            $"Tentativa de excluir '{entry.Entity.GetType().Name}' fora do Tenant atual.");
                    break;
            }
        }
    }

    /// <summary>Preenche UpdatedAt (UTC) em toda entidade modificada nesta unidade de trabalho.</summary>
    private void ApplyAuditTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}