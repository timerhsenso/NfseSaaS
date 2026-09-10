using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Metadado de um arquivo anexado a um Contrato (documentação assinada,
/// cópia do contrato, etc.). O arquivo em si NÃO fica no Postgres — vive
/// em disco, organizado por Tenant/Empresa/Contrato (ver
/// ContratoDocumentoFileStore), sem criptografia (decisão do usuário:
/// diferente do certificado .pfx, documento de contrato não precisa).
///
/// EmpresaId é desnormalizado aqui (também dá pra chegar nele via
/// Contrato) só pra montar o caminho em disco sem precisar de um JOIN
/// extra em toda operação de arquivo.
///
/// Nome físico em disco = Id deste registro (GUID) + Extensao — nunca o
/// NomeOriginal, que fica só aqui pra exibição (evita path traversal e
/// colisão de nome).
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class ContratoDocumento : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public Guid ContratoId { get; set; }

    public string NomeOriginal { get; set; } = string.Empty;

    /// <summary>Com o ponto, ex.: ".pdf", ".docx" — mesmo valor usado no nome físico do arquivo.</summary>
    public string Extensao { get; set; } = string.Empty;

    public long TamanhoBytes { get; set; }
}
