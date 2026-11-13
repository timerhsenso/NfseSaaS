using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Tomador de serviço (cliente) de uma Empresa dentro do Tenant.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Cliente : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Empresa dona do cadastro ("Só esta empresa"). NULO = compartilhado,
    /// disponível para TODAS as empresas da conta (C10). Na mesma empresa,
    /// um exclusivo com o mesmo CPF/CNPJ vence o compartilhado (ver
    /// ClientesDaEmpresa.Visiveis). Contas diferentes nunca se enxergam.
    /// </summary>
    public Guid? EmpresaId { get; set; }

    /// <summary>true = disponível para todas as empresas da conta.</summary>
    public bool Compartilhado => EmpresaId is null;

    /// <summary>CPF ou CNPJ do tomador, apenas dígitos.</summary>
    public string CpfCnpj { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    /// <summary>
    /// Nome curto de exibição, usado no lugar de Nome em TODA tela,
    /// e-mail interno e nome de arquivo (PDF do DANFSe). Obrigatório e
    /// único na lista de cada Empresa, contando os compartilhados (sem
    /// diferenciar maiúsculas/minúsculas; ver ApelidoCliente).
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
    /// true = mandar a nota autorizada (PDF e XML) por e-mail a este tomador
    /// logo depois da autorização. Padrão: NÃO manda — quem liga precisa
    /// informar o e-mail (o validator exige). Substituiu o antigo
    /// NaoEnviarNfsePorEmail ("pelo avesso"), que fazia todo cliente com
    /// e-mail receber sem ninguém ter pedido.
    /// </summary>
    public bool EnviarNfsePorEmail { get; set; }

    /// <summary>
    /// Até 5 endereços que recebem o mesmo e-mail da nota, em cópia,
    /// separados por ";" (ver EmailsCopiaCliente). Nulo = sem cópia. Só
    /// valem com EnviarNfsePorEmail ligado (ficam guardados se desligar).
    /// </summary>
    public string? EmailsCopia { get; set; }

    /// <summary>true = cópias em Cco (ocultas); false = Cc (todos veem quem recebeu).</summary>
    public bool CopiaOculta { get; set; }

    /// <summary>Modelo do e-mail da nota só deste cliente. Nulo = o da empresa (ou o padrão da conta).</summary>
    public Guid? ModeloEmailNotaId { get; set; }
}
