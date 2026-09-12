namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Cria os 5 grupos de permissão padrão (Administrador/Emissor/
/// Financeiro/Consulta/Contador) — com a matriz IAEC equivalente ao que
/// os antigos papéis do Identity faziam — pra um Tenant. Chamado tanto
/// em Registrar (Tenant novo) quanto no backfill de startup (Tenants que
/// já existiam antes do módulo de segurança).
/// </summary>
public interface IGrupoProvisionamentoService
{
    /// <returns>Nome do grupo padrão → Id do Grupo criado, pra quem chamou saber qual Id usar (ex.: atribuir o primeiro usuário ao grupo "Administrador").</returns>
    Task<IReadOnlyDictionary<string, Guid>> ProvisionarGruposPadraoAsync(Guid tenantId, CancellationToken cancellationToken);
}
