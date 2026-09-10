namespace NfseSaaS.Nacional.Models;

/// <summary>
/// Dados necessários para montar e assinar o evento de cancelamento
/// (e101101) de uma NFS-e já autorizada, leiaute pedRegEvento/infPedReg
/// da SEFIN Nacional (schema 1.00).
/// </summary>
/// <param name="TpAmb">
/// tpAmb do evento: "1" = Produção, "2" = Homologação — mesma regra e
/// mesmo motivo do DpsRequest.TpAmb: precisa ser o ambiente da Empresa
/// dona da nota sendo cancelada, não um valor global do appsettings.
/// </param>
public sealed record EventoCancelamentoRequest(
    string ChaveAcesso,
    string CnpjAutor,
    int CodigoMotivo,
    string Motivo,
    string TpAmb);
