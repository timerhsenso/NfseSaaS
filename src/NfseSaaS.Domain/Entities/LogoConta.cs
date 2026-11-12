using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Logo da conta (B9 — plano Escritório): o escritório de contabilidade
/// coloca a própria marca no topo do menu e nas impressões, para os
/// clientes que ele convida verem o "sistema do escritório".
///
/// Uma por conta. A imagem fica no banco (é pequena, até 512 KB, e assim
/// vai junto no backup sem depender de volume em disco) e numa tabela
/// separada, para não pesar nas consultas da conta (Tenant). Versao é o
/// começo do SHA-256 do conteúdo: entra na URL e deixa o navegador guardar
/// a imagem em cache sem nunca mostrar a antiga depois de trocar.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class LogoConta : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public byte[] Conteudo { get; set; } = Array.Empty<byte>();

    /// <summary>image/png, image/jpeg ou image/webp (conferido pelo conteúdo, não pela extensão).</summary>
    public string TipoConteudo { get; set; } = string.Empty;

    public string Versao { get; set; } = string.Empty;

    public long TamanhoBytes { get; set; }
}
