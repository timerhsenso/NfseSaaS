namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>DTO de entrada para o caso de uso de emissão de NFS-e (implementação na Fase 2).</summary>
public sealed record EmitirNfseRequest(
    Guid EmpresaId,
    Guid ClienteId,
    Guid ServicoId,
    decimal ValorServico,
    string DescricaoServico,
    DateOnly DataCompetencia);
