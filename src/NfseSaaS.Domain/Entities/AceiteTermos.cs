using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Prova do aceite dos Termos de uso e da Política de privacidade por um
/// usuário: quais versões, quando, de onde (IP e navegador) e em que
/// momento (cadastro, convite ou nova versão). Um registro por aceite,
/// NUNCA alterado nem apagado pela aplicação — nem quando o usuário é
/// excluído (AspNetUsers não tem FK pra cá de propósito), porque é a
/// evidência do consentimento.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId. Nos
/// fluxos anônimos (cadastro e aceite de convite, antes do login) o TenantId
/// vem de AppDbContext.TenantIdOverrideDeSistema.
/// </summary>
public sealed class AceiteTermos : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid UsuarioId { get; set; }

    public string VersaoTermosDeUso { get; set; } = string.Empty;

    public string VersaoPoliticaPrivacidade { get; set; } = string.Empty;

    public DateTimeOffset AceitoEm { get; set; } = DateTimeOffset.UtcNow;

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Onde o aceite aconteceu — ver <see cref="OrigemAceiteTermos"/>.</summary>
    public string Origem { get; set; } = string.Empty;
}

/// <summary>Valores de <see cref="AceiteTermos.Origem"/>.</summary>
public static class OrigemAceiteTermos
{
    /// <summary>Criação da conta (primeiro usuário do Tenant).</summary>
    public const string Cadastro = "Cadastro";

    /// <summary>Usuário convidado definindo a senha.</summary>
    public const string Convite = "Convite";

    /// <summary>Tela de aceite: usuário existente sem aceite da versão vigente.</summary>
    public const string Atualizacao = "Atualizacao";
}
