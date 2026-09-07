namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// DTO de entrada para cancelamento de NFS-e. CodigoMotivo é o código da
/// tabela oficial de motivos de cancelamento da NFS-e Nacional — não
/// validamos aqui contra uma lista fechada de códigos porque não temos
/// certeza do conteúdo completo dessa tabela; a própria SEFIN rejeita um
/// código inválido (a rejeição chega como erro no CancelarNfseResult).
/// </summary>
public sealed record CancelarNfseRequest(int CodigoMotivo, string Motivo);
