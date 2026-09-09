namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>DTO de entrada para o caso de uso de emissão de NFS-e (implementação na Fase 2).</summary>
/// <param name="IdempotencyKey">
/// Opcional. Se o chamador reenviar a mesma requisição com a mesma chave
/// (ex.: retry após timeout de rede), a emissão original é retornada em
/// vez de criar uma segunda Nfse.
/// </param>
public sealed record EmitirNfseRequest(
    Guid EmpresaId,
    Guid ClienteId,
    Guid ServicoId,
    decimal ValorServico,
    string DescricaoServico,
    DateOnly DataCompetencia,
    string? IdempotencyKey = null);
