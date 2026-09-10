namespace NfseSaaS.Nacional.Models;

/// <summary>
/// Dados necessários para montar e assinar o evento de cancelamento
/// (e101101) de uma NFS-e já autorizada, leiaute pedRegEvento/infPedReg
/// da SEFIN Nacional (schema 1.00).
/// </summary>
public sealed record EventoCancelamentoRequest(
    string ChaveAcesso,
    string CnpjAutor,
    int CodigoMotivo,
    string Motivo);
