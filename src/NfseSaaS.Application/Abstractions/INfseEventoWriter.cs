using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Registra um evento no histórico de uma Nfse (ver NfseEvento) dentro da
/// MESMA unidade de trabalho da operação que o originou — Registrar() só
/// adiciona ao ChangeTracker, NUNCA chama SaveChanges por conta própria.
/// Mesmo raciocínio de atomicidade de <see cref="IAuditLogWriter"/>: se a
/// operação principal falhar antes do SaveChanges, o evento também não é
/// persistido.
///
/// TenantId é preenchido automaticamente pelo AppDbContext
/// (ApplyTenantIsolation) — quem chama Registrar() só informa o que é
/// específico do evento.
/// </summary>
public interface INfseEventoWriter
{
    /// <param name="nfseId">Id da Nfse a que este evento pertence.</param>
    /// <param name="tipo">Tipo do evento — ver NfseEventoTipo.</param>
    /// <param name="codigo">Código de erro/rejeição da SEFIN, quando houver.</param>
    /// <param name="mensagem">Mensagem/descrição do evento, quando houver.</param>
    void Registrar(Guid nfseId, NfseEventoTipo tipo, string? codigo = null, string? mensagem = null);
}
