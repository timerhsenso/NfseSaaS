namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Aceite dos Termos de uso e da Política de privacidade (versões em
/// NfseSaaS.Domain.Legal.DocumentosLegais). Mesmo contrato do
/// IAuditLogWriter: <see cref="Registrar"/> só adiciona ao ChangeTracker,
/// quem chama decide quando salvar (o cadastro grava o aceite na mesma
/// transação do Tenant e do usuário).
/// </summary>
public interface IAceiteTermosService
{
    /// <summary>
    /// True se o aceite do usuário libera o uso HOJE: a versão atual, ou a
    /// anterior enquanto a atual ainda não começou a valer (aviso prévio, ver
    /// DocumentosLegais). Resposta positiva fica em cache.
    /// </summary>
    Task<bool> AceitouVersaoVigenteAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>True se o usuário já aceitou exatamente a versão ATUAL (mesmo antes de ela valer).</summary>
    Task<bool> AceitouVersaoAtualAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>
    /// Adiciona o registro do aceite das versões vigentes (com IP e
    /// navegador da requisição atual) e a entrada de auditoria
    /// "AceitarTermos". NÃO salva.
    /// </summary>
    /// <param name="origem">Ver NfseSaaS.Domain.Entities.OrigemAceiteTermos.</param>
    void Registrar(Guid usuarioId, string origem);

    /// <summary>Chamar DEPOIS do SaveChanges que gravou o aceite: evita a consulta ao banco nas próximas requisições do usuário.</summary>
    void MarcarComoAceito(Guid usuarioId);
}
