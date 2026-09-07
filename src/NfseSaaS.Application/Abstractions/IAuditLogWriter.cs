namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Registra uma entrada de auditoria (AuditLog) dentro da MESMA unidade de
/// trabalho da operação que a originou — Registrar() só adiciona ao
/// ChangeTracker, NUNCA chama SaveChanges por conta própria. Isso garante
/// atomicidade: se a operação principal falhar antes do SaveChanges, o
/// registro de auditoria também não é persistido (não fica um log de algo
/// que não aconteceu).
///
/// TenantId/UserId/IpAddress são preenchidos automaticamente (TenantId via
/// AppDbContext.ApplyTenantIsolation, UserId/IpAddress via ICurrentUser) —
/// quem chama Registrar() só informa o que é específico da operação.
/// </summary>
public interface IAuditLogWriter
{
    /// <param name="operacao">Nome da operação (ex.: "CadastrarEmpresa", "CancelarNfse").</param>
    /// <param name="entidade">Nome da entidade afetada (ex.: "Empresa", "Nfse").</param>
    /// <param name="entidadeId">Id da entidade afetada, quando aplicável.</param>
    /// <param name="dados">
    /// Dados não sensíveis sobre a operação, serializados como JSON. NUNCA
    /// passar senha, senha de certificado, chave privada ou XML fiscal
    /// completo — ver o mesmo aviso na entidade AuditLog.
    /// </param>
    void Registrar(string operacao, string entidade, Guid? entidadeId, object? dados = null);
}
