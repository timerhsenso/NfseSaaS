using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Uma célula da matriz de permissão: o que um Grupo pode fazer numa
/// Tela específica — padrão IAEC (Incluir/Alterar/Excluir/Consultar).
/// Uma tela sem linha aqui pra um Grupo == sem nenhum acesso (nem
/// leitura) — a ausência de linha é "tudo false", não é preciso gravar
/// uma linha zerada explicitamente (ver GrupoProvisionamentoService).
///
/// TenantId é DUPLICADO aqui (não vem só de Grupo) — mesmo padrão já
/// usado em ContratoServico/ReajusteContrato/ContratoDocumento: permite
/// o Global Query Filter funcionar direto nesta tabela sem precisar de
/// join até Grupo, e é o que sustenta o índice único (TenantId, GrupoId,
/// TelaId) contra duas linhas pro mesmo par Grupo/Tela.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class GrupoTela : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid GrupoId { get; set; }

    public Guid TelaId { get; set; }

    public bool Incluir { get; set; }

    public bool Alterar { get; set; }

    public bool Excluir { get; set; }

    public bool Consultar { get; set; }
}
