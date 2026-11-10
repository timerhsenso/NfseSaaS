using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Quais clientes uma empresa enxerga (C10). Cliente.EmpresaId nulo =
/// compartilhado por todas as empresas da conta; o filtro global por conta
/// (TenantId) continua valendo nas duas consultas, então contas diferentes
/// nunca se misturam.
/// </summary>
internal static class ClientesDaEmpresa
{
    /// <summary>
    /// Lista da empresa: os exclusivos dela + os compartilhados que ela NÃO
    /// tem como exclusivo (mesmo CPF/CNPJ). O exclusivo vence, então o mesmo
    /// documento nunca aparece duas vezes. Vira um NOT EXISTS no Postgres.
    /// </summary>
    public static IQueryable<Cliente> Visiveis(AppDbContext db, Guid empresaId) =>
        db.Clientes.Where(c =>
            c.EmpresaId == empresaId ||
            (c.EmpresaId == null && !db.Clientes.Any(x => x.EmpresaId == empresaId && x.CpfCnpj == c.CpfCnpj)));

    /// <summary>
    /// Cliente que pode ir numa nota ou contrato da empresa: exclusivo dela
    /// ou compartilhado. Mais largo que <see cref="Visiveis"/> de propósito:
    /// contrato antigo de um compartilhado continua emitindo mesmo depois que
    /// a empresa ganhou um exclusivo com o mesmo documento.
    /// </summary>
    public static IQueryable<Cliente> Utilizaveis(AppDbContext db, Guid empresaId) =>
        db.Clientes.Where(c => c.EmpresaId == empresaId || c.EmpresaId == null);
}
