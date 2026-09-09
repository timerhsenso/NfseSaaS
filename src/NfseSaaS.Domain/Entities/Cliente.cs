using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Tomador de serviço (cliente) de uma Empresa dentro do Tenant.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Cliente : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    /// <summary>CPF ou CNPJ do tomador, apenas dígitos.</summary>
    public string CpfCnpj { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Telefone { get; set; }

    /// <summary>Código do município (IBGE) do endereço do tomador.</summary>
    public string CodigoMunicipio { get; set; } = string.Empty;

    public string Cep { get; set; } = string.Empty;

    public string Logradouro { get; set; } = string.Empty;

    public string Numero { get; set; } = string.Empty;

    public string? Complemento { get; set; }

    public string Bairro { get; set; } = string.Empty;

    /// <summary>UF (sigla, 2 letras) do endereço — mesmo raciocínio do campo equivalente em Empresa.</summary>
    public string Uf { get; set; } = string.Empty;

    public bool Ativo { get; set; } = true;
}
