using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Serviço cadastrado por uma Empresa, usado como base para a emissão da DPS.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Servico : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public string Descricao { get; set; } = string.Empty;

    /// <summary>Código de tributação nacional (cTribNac) do layout da NFS-e Nacional.</summary>
    public string CodigoTributacaoNacional { get; set; } = string.Empty;

    /// <summary>Código NBS (Nomenclatura Brasileira de Serviços), campo cNBS da DPS.</summary>
    public string CodigoNbs { get; set; } = string.Empty;

    /// <summary>
    /// IBS/CBS (Reforma Tributária): indicador da operação (cIndOp, Anexo VII).
    /// Vazio = vale a sugestão da tabela oficial para o item (RegrasIbsCbs).
    /// </summary>
    public string? CIndOp { get; set; }

    /// <summary>
    /// IBS/CBS: classificação tributária (cClassTrib, 6 dígitos; o CST são os
    /// 3 primeiros). Vazio = vale a sugestão da tabela oficial para o item.
    /// </summary>
    public string? CClassTrib { get; set; }

    public decimal ValorPadrao { get; set; }

    public bool Ativo { get; set; } = true;
}
