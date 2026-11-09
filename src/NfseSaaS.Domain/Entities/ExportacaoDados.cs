using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Pedido de exportação de TODOS os dados da conta (D1, portabilidade da
/// LGPD): um arquivo ZIP com cadastros (CSV e JSON), notas (XML e PDF) e
/// documentos dos contratos, montado em segundo plano. O arquivo fica fora
/// do banco e do backup e é apagado quando expira.
/// Entidade TENANT-SCOPED.
/// </summary>
public sealed class ExportacaoDados : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Administrador que pediu (recebe o aviso por e-mail). Guid solto, sem FK.</summary>
    public Guid SolicitadoPorUsuarioId { get; set; }

    public StatusExportacaoDados Status { get; set; } = StatusExportacaoDados.Pendente;

    public DateTimeOffset? ConcluidaEm { get; set; }

    /// <summary>Até quando o arquivo pode ser baixado (só quando Pronta).</summary>
    public DateTimeOffset? ExpiraEm { get; set; }

    public long? TamanhoBytes { get; set; }

    /// <summary>Notas incluídas (todas as situações; XML/PDF só das autorizadas e canceladas).</summary>
    public int? QuantidadeNotas { get; set; }

    /// <summary>Última vez que alguém baixou o arquivo.</summary>
    public DateTimeOffset? BaixadaEm { get; set; }

    /// <summary>Motivo da falha, para o usuário e o operador.</summary>
    public string? Erro { get; set; }
}
