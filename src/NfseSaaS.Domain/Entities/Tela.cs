using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Catálogo das telas/módulos do sistema (Empresas, Clientes, Nfse,
/// Usuários...) — módulo de segurança IAEC (Incluir/Alterar/Excluir/
/// Consultar), mesmo padrão já usado no RhSensoERP. É a "coluna" da
/// matriz de permissão (ver GrupoTela).
///
/// Entidade GLOBAL (não implementa ITenantEntity, não sofre o Global
/// Query Filter) — é o próprio código que define quais telas existem
/// (ver TelaCatalogo), não o usuário de um Tenant específico. Seedada
/// no startup, mesmo mecanismo do IdentitySeeder pros papéis do Identity.
/// </summary>
public sealed class Tela : BaseEntity
{
    /// <summary>Identificador estável, referenciado no código via TelaCatalogo — NUNCA muda depois de criado (é o que GrupoTela referencia).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    public int Ordem { get; set; }
}
