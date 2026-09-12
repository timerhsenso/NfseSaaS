using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Grupo de permissão configurável — substitui os antigos papéis fixos
/// do Identity (Administrador/Emissor/Financeiro/Consulta/Contador) como
/// fonte de autorização de tela. Cada Tenant tem os seus próprios Grupos
/// (ver GrupoProvisionamentoService), inclusive os 5 padrão, que
/// continuam existindo mas agora são só a semente inicial — totalmente
/// editáveis, e o Tenant pode criar outros além desses 5.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Grupo : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string? Descricao { get; set; }

    /// <summary>
    /// True só para os 5 grupos semeados automaticamente na criação do
    /// Tenant — não impede edição da matriz de permissão, só marca a
    /// origem (não é o usuário quem criou). Nomenclatura livre não muda
    /// esta flag.
    /// </summary>
    public bool Padrao { get; set; }

    /// <summary>
    /// True só para o grupo administrador padrão de cada Tenant — é o que
    /// a regra "não pode ficar sem Administrador" (ver
    /// EhUnicoAdministradorAsync) usa pra proteger o Tenant de ficar sem
    /// ninguém capaz de gerenciar usuário/permissão. Calculado a partir
    /// da matriz seria frágil (bastaria zerar uma permissão da matriz pra
    /// escapar da checagem) — por isso é uma flag própria, não derivada.
    /// </summary>
    public bool EhAdministrador { get; set; }
}
