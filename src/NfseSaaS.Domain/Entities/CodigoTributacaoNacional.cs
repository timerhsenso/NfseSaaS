using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Catálogo oficial do Código de Tributação Nacional (cTribNac) usado na
/// NFS-e Nacional — 338 códigos, derivados da lista de serviços da LC
/// 116/2003, publicados pela Receita Federal/Comitê Gestor da NFS-e
/// (gov.br/nfse, Biblioteca > Documentação Técnica).
///
/// Entidade GLOBAL: NÃO implementa ITenantEntity — é o mesmo catálogo
/// pra todo Tenant (igual a <see cref="Tenant"/>, a única outra entidade
/// que já foge do filtro multi-tenant). Só leitura pela aplicação —
/// alimentada por CodigoTributacaoNacionalSeeder no startup, nunca por
/// CRUD de usuário (é uma tabela do governo, não um cadastro do SaaS).
/// </summary>
public sealed class CodigoTributacaoNacional : BaseEntity
{
    /// <summary>6 dígitos, sem pontuação (ex.: "010101" — exibido como 01.01.01).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;
}
