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

    /// <summary>
    /// Nome curto de exibição, usado no lugar de Nome em TODA tela,
    /// e-mail interno e nome de arquivo (PDF do DANFSe). Obrigatório e
    /// único por Empresa (sem diferenciar maiúsculas/minúsculas).
    ///
    /// NUNCA entra no documento fiscal (DPS/XML, conteúdo do DANFSe,
    /// snapshot): ali vale só Nome (razão social), que é o dado oficial.
    /// </summary>
    public string Apelido { get; set; } = string.Empty;

    public const int TamanhoMaximoApelido = 40;

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

    /// <summary>
    /// Este tomador retém o ISS na fonte (órgão público, PJ que a lei
    /// municipal define como substituta tributária etc.). É o padrão da nota
    /// emitida pra ele — dá pra mudar na hora de emitir. Quem decide a
    /// retenção é o tomador, por isso fica no Cliente e não na Empresa.
    /// </summary>
    public bool RetemIss { get; set; }

    /// <summary>
    /// true = NÃO mandar a nota autorizada (PDF e XML) por e-mail a este
    /// tomador. O padrão é mandar sempre que houver e-mail cadastrado.
    /// Gravado "pelo avesso" de propósito: com false como padrão, os
    /// clientes que já existem passam a receber sem precisar de valor
    /// padrão no banco (o EF ignora HasDefaultValue(true) quando o valor
    /// em memória é false — o mesmo aviso de "sentinel" de outros campos).
    /// </summary>
    public bool NaoEnviarNfsePorEmail { get; set; }
}
