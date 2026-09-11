namespace NfseSaaS.Application.UseCases.NotaMensal;

public interface IEmitirNotaMensalLoteUseCase
{
    /// <summary>Sequencial, item a item — nunca aborta o lote por causa de uma falha isolada (ver comentário na implementação).</summary>
    Task<IReadOnlyList<ItemResultadoNotaMensalResponse>> ExecutarAsync(EmitirNotaMensalLoteRequest request, CancellationToken cancellationToken);
}
