namespace NfseSaaS.Nacional.Responses;

/// <summary>Item de erro/rejeição retornado pela SEFIN Nacional.</summary>
public sealed record NfseErro(string Codigo, string Descricao);

/// <summary>
/// Representa a resposta HTTP (200/201 ou 4xx) do endpoint POST /nfse da
/// SEFIN Nacional, já desserializada. Espelha os campos observados na POC:
/// tipoAmbiente, versaoAplicativo, dataHoraProcessamento, idDps/chaveAcesso,
/// nfseXmlGZipB64 (sucesso) e erros (rejeição).
/// </summary>
public sealed record NfseNacionalResponse(
    bool Sucesso,
    int? TipoAmbiente,
    string? VersaoAplicativo,
    DateTimeOffset? DataHoraProcessamento,
    string? IdDps,
    string? ChaveAcesso,
    string? NfseXmlGZipB64,
    IReadOnlyCollection<NfseErro> Erros);
