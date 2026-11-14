using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Legal;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// JUR1: prova do aceite de um instrumento legal com aceite próprio (DPA,
/// Autorização do certificado A1, Autorização de emissão automática): quem,
/// em nome de qual conta e empresa, qual versão, o hash do texto aceito,
/// quando e de onde. Termos e Política continuam em <see cref="AceiteTermos"/>.
///
/// Nunca é apagado pela aplicação enquanto a conta existe (é evidência);
/// as autorizações podem ser REVOGADAS (remoção do certificado, automação
/// desligada), o que só preenche <see cref="RevogadoEm"/>. Sem FK para
/// usuário ou empresa, de propósito: a prova continua se eles forem excluídos.
///
/// Entidade TENANT-SCOPED (filtro global por TenantId).
/// </summary>
public sealed class AceiteInstrumento : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid UsuarioId { get; set; }

    /// <summary>E-mail do usuário no momento do aceite (o usuário pode mudar ou ser excluído depois).</summary>
    public string UsuarioEmail { get; set; } = string.Empty;

    /// <summary>Perfil de quem aceitou: "Administrador" ou o nome do grupo de permissão.</summary>
    public string PerfilUsuario { get; set; } = string.Empty;

    public TipoDocumentoLegal Tipo { get; set; }

    public string Versao { get; set; } = string.Empty;

    /// <summary>SHA-256 (hexadecimal) do texto aceito (Application/Legal/TextosInstrumentos).</summary>
    public string HashTexto { get; set; } = string.Empty;

    /// <summary>Empresa da autorização (A1 e emissão automática). Nulo no DPA, que é da conta.</summary>
    public Guid? EmpresaId { get; set; }

    /// <summary>CNPJ da empresa no momento do aceite.</summary>
    public string? CnpjEmpresa { get; set; }

    /// <summary>Autorização A1: impressão digital do certificado autorizado (nunca a senha).</summary>
    public string? CertificadoImpressaoDigital { get; set; }

    /// <summary>Emissão automática: configuração da automação no momento do aceite (JSON curto).</summary>
    public string? Detalhes { get; set; }

    public DateTimeOffset AceitoEm { get; set; } = DateTimeOffset.UtcNow;

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Onde foi aceito: Cadastro, Atualizacao (tela da versão nova), Certificado, Automacao.</summary>
    public string Origem { get; set; } = string.Empty;

    public DateTimeOffset? RevogadoEm { get; set; }

    public Guid? RevogadoPorUsuarioId { get; set; }

    public string? MotivoRevogacao { get; set; }

    public bool Vigente => RevogadoEm is null;
}

/// <summary>Valores de <see cref="AceiteInstrumento.Origem"/>.</summary>
public static class OrigemAceiteInstrumento
{
    public const string Cadastro = "Cadastro";
    public const string Atualizacao = "Atualizacao";
    public const string Certificado = "Certificado";
    public const string Automacao = "Automacao";
}
