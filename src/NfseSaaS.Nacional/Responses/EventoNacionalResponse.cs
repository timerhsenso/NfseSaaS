namespace NfseSaaS.Nacional.Responses;

/// <summary>
/// Estrutura de erro da SEFIN Nacional (schema oficial "MensagemProcessamento",
/// confirmado no Swagger publicado em sefin.producaorestrita.nfse.gov.br —
/// campos exatos: codigo, descricao, complemento). Usada pelo endpoint de
/// eventos, que devolve UM erro (objeto), diferente do endpoint /nfse
/// principal, que devolve uma lista "erros" (ver NfseErro).
/// </summary>
public sealed record NfseMensagemProcessamento(string? Codigo, string? Descricao, string? Complemento);

/// <summary>
/// Representa a resposta HTTP (201 ou 4xx) do endpoint POST /nfse/{chaveAcesso}/eventos
/// da SEFIN Nacional, já desserializada — schema oficial "EventosPostResponseSucesso" /
/// "ResponseErro" (confirmado contra o Swagger publicado pela própria SEFIN Nacional).
/// </summary>
public sealed record EventoNacionalResponse(
    bool Sucesso,
    int? TipoAmbiente,
    string? VersaoAplicativo,
    DateTimeOffset? DataHoraProcessamento,
    string? EventoXmlGZipB64,
    NfseMensagemProcessamento? Erro);
