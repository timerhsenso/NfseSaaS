namespace NfseSaaS.Domain.Entities;

/// <summary>
/// CNPJ que já usou o teste grátis em produção (um teste por CNPJ). Gravado
/// quando o teste de uma conta de perfil Empresa começa, com o CNPJ de cada
/// empresa dela. Sem chave estrangeira de propósito: continua valendo depois
/// que a conta é excluída (senão bastaria excluir e criar outra conta).
/// Contas de contador não gravam nem consultam: cadastram CNPJs de clientes,
/// que podem ter conta própria.
/// </summary>
public sealed class TesteGratisCnpj
{
    /// <summary>Só os 14 dígitos.</summary>
    public string Cnpj { get; set; } = string.Empty;

    /// <summary>Conta que usou o teste com este CNPJ (pode já ter sido excluída).</summary>
    public Guid TenantId { get; set; }

    public DateTimeOffset IniciadoEm { get; set; }
}
