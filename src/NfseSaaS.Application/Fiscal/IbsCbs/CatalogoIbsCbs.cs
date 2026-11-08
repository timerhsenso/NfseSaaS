using System.Collections.Frozen;
using System.Text.Json;

namespace NfseSaaS.Application.Fiscal.IbsCbs;

/// <summary>Indicador da operação (cIndOp) do Anexo VII.</summary>
/// <param name="Local">Local de incidência do IBS que o indicador define (coluna NFSeLocIncidIBS).</param>
/// <param name="Nfse">Admitido na NFS-e (coluna indNFSe = "S").</param>
public sealed record IndicadorOperacao(string Codigo, string Tipo, string Caracteristica, string Local, bool Nfse)
{
    /// <summary>
    /// O micoempresa consegue emitir com este indicador? Só os que não
    /// pedem grupos que a DPS ainda não leva: imóvel (gUnidImob, E0938),
    /// evento, destinatário diferente do tomador (dest, E0910) e transporte.
    /// </summary>
    public bool SuportadoPeloSistema =>
        Nfse && Local is "Estabelecimento do Prestador/Fornecedor" or "Endereço do Tomador/Adquirente" or "Local da Prestação";

    /// <summary>Texto curto para a tela ("100301 — Demais serviços... (Endereço do Tomador/Adquirente)").</summary>
    public string Rotulo => $"{Codigo} — {Tipo}: {Caracteristica} (local: {Local})";
}

/// <summary>Classificação tributária (cClassTrib) com o nome oficial.</summary>
public sealed record ClassificacaoTributaria(string Codigo, string Nome)
{
    /// <summary>CST = 3 primeiros dígitos do cClassTrib (regra E0959).</summary>
    public string Cst => Codigo[..3];

    public string Rotulo => $"{Codigo} — {Nome} (CST {Cst})";
}

/// <summary>Opções do Anexo VIII para um item da LC 116 (os 4 primeiros dígitos do cTribNac).</summary>
public sealed record OpcoesIbsCbsDoItem(
    string Item,
    string Descricao,
    IReadOnlyList<string> Nbs,
    IReadOnlyList<IndicadorOperacao> IndicadoresOperacao,
    IReadOnlyList<ClassificacaoTributaria> Classificacoes,
    IReadOnlyList<string> AtividadesSimplesNacional);

/// <summary>
/// Tabelas oficiais da Reforma Tributária na NFS-e, embutidas no sistema
/// (gerado por tools/ibscbs/gerar_correlacao.py a partir dos Anexos VII e
/// VIII do portal da NFS-e). Só leitura, carregado uma vez. Para atualizar,
/// rode o script com as planilhas novas e suba o JSON.
/// </summary>
public static class CatalogoIbsCbs
{
    private const string NomeRecurso = "NfseSaaS.Application.Fiscal.IbsCbs.correlacao-ibscbs.json";

    private static readonly Lazy<Dados> _dados = new(Carregar);

    public static string VersaoAnexoVII => _dados.Value.VersaoAnexoVII;

    public static string VersaoAnexoVIII => _dados.Value.VersaoAnexoVIII;

    public static IReadOnlyCollection<IndicadorOperacao> IndicadoresOperacao => _dados.Value.IndOps.Values;

    public static IReadOnlyCollection<ClassificacaoTributaria> Classificacoes => _dados.Value.Classificacoes.Values;

    public static IndicadorOperacao? IndicadorOperacao(string? codigo) =>
        Digitos(codigo) is { Length: 6 } c && _dados.Value.IndOps.TryGetValue(c, out var i) ? i : null;

    public static ClassificacaoTributaria? Classificacao(string? codigo) =>
        Digitos(codigo) is { Length: 6 } c && _dados.Value.Classificacoes.TryGetValue(c, out var i) ? i : null;

    /// <summary>Opções do item do cTribNac ("010501" → item "0105"); nulo se o item não está no Anexo VIII.</summary>
    public static OpcoesIbsCbsDoItem? OpcoesDoServico(string? codigoTributacaoNacional) =>
        Digitos(codigoTributacaoNacional) is { Length: >= 4 } c && _dados.Value.Itens.TryGetValue(c[..4], out var o) ? o : null;

    internal static string? Digitos(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : new string(valor.Where(char.IsDigit).ToArray());

    private sealed record Dados(
        string VersaoAnexoVII,
        string VersaoAnexoVIII,
        FrozenDictionary<string, IndicadorOperacao> IndOps,
        FrozenDictionary<string, ClassificacaoTributaria> Classificacoes,
        FrozenDictionary<string, OpcoesIbsCbsDoItem> Itens);

    private sealed record ArquivoJson(string VersaoAnexoVII, string VersaoAnexoVIII, List<IndOpJson> IndOps, List<ClassTribJson> CClassTribs, List<ItemJson> Itens);

    private sealed record IndOpJson(string Codigo, string Tipo, string Caracteristica, string Local, bool Nfse);

    private sealed record ClassTribJson(string Codigo, string Nome);

    private sealed record ItemJson(string Item, string Descricao, List<string> Nbs, List<string> IndOps, List<string> CClassTribs, List<string> AtividadesSn);

    private static Dados Carregar()
    {
        using var recurso = typeof(CatalogoIbsCbs).Assembly.GetManifestResourceStream(NomeRecurso)
            ?? throw new InvalidOperationException($"Recurso {NomeRecurso} não encontrado no assembly.");

        var arquivo = JsonSerializer.Deserialize<ArquivoJson>(recurso, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Recurso {NomeRecurso} vazio.");

        var indOps = arquivo.IndOps
            .Select(i => new IndicadorOperacao(i.Codigo, i.Tipo, i.Caracteristica, i.Local, i.Nfse))
            .ToFrozenDictionary(i => i.Codigo);
        var classificacoes = arquivo.CClassTribs
            .Select(c => new ClassificacaoTributaria(c.Codigo, c.Nome))
            .ToFrozenDictionary(c => c.Codigo);
        var itens = arquivo.Itens.ToFrozenDictionary(
            i => i.Item,
            i => new OpcoesIbsCbsDoItem(
                i.Item,
                i.Descricao,
                i.Nbs,
                i.IndOps.Where(indOps.ContainsKey).Select(c => indOps[c]).ToList(),
                i.CClassTribs.Where(classificacoes.ContainsKey).Select(c => classificacoes[c]).ToList(),
                i.AtividadesSn));

        return new Dados(arquivo.VersaoAnexoVII, arquivo.VersaoAnexoVIII, indOps, classificacoes, itens);
    }
}
