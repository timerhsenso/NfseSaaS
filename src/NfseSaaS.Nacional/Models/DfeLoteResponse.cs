namespace NfseSaaS.Nacional.Models;

/// <summary>
/// Resposta real de GET {baseUrl}/DFe/{nsu} — confirmada contra uma
/// chamada real ao ambiente de produção restrita em 09/09/2026. Nomes de
/// propriedade em PascalCase batem exatamente com o JSON retornado (o
/// serializador padrão do ASP.NET Core já lida com isso via
/// case-insensitive binding, mas os nomes aqui documentam o contrato real
/// pra quem for ler o código, não uma suposição).
/// </summary>
public sealed record DfeLoteResponse(
    string StatusProcessamento,
    List<DfeItem> LoteDFe,
    List<string> Alertas,
    List<string> Erros,
    string TipoAmbiente,
    string VersaoAplicativo,
    DateTimeOffset DataHoraProcessamento);

public sealed record DfeItem(
    long NSU,
    string ChaveAcesso,
    string TipoDocumento,
    string ArquivoXml,
    DateTimeOffset DataHoraGeracao);
