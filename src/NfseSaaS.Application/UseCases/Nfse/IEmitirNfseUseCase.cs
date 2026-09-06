namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Caso de uso: emitir uma NFS-e a partir de um serviço prestado.
/// Implementação prevista para a Fase 2 — orquestra Infrastructure
/// (persistência) e NfseSaaS.Nacional (integração fiscal) através de suas
/// abstrações, sem que a Application conheça detalhes de EF Core/HTTP.
/// </summary>
public interface IEmitirNfseUseCase
{
    Task<EmitirNfseResult> ExecutarAsync(EmitirNfseRequest request, CancellationToken cancellationToken);
}
