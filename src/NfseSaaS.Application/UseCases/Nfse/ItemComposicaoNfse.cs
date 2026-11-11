namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Quanto um serviço do Contrato valeu numa nota (a NFS-e só leva o total).
/// Gravado em Nfse.ComposicaoValoresJson para a Nota Mensal sugerir o
/// último valor de cada serviço.
/// </summary>
public sealed record ItemComposicaoNfse(Guid ContratoServicoId, Guid ServicoId, string Descricao, decimal Valor);
