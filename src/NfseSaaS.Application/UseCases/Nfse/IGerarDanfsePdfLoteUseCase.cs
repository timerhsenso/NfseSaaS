namespace NfseSaaS.Application.UseCases.Nfse;

public interface IGerarDanfsePdfLoteUseCase
{
    Task<DanfsePdfLoteResponse> ExecutarAsync(GerarDanfsePdfLoteRequest request, CancellationToken cancellationToken);
}
