namespace NfseSaaS.Application.UseCases.Importacao;

/// <summary>
/// IM1: uma célula da planilha como veio do arquivo. A leitura (.xlsx ou
/// .csv) fica na Infrastructure; as regras de conversão (RegrasImportacao)
/// trabalham só com isto, para serem testadas sem arquivo.
/// </summary>
/// <param name="Texto">O que a pessoa vê na célula (aparado). Vazio = célula vazia.</param>
/// <param name="Numero">Valor numérico quando a célula do Excel é número (o texto pode ter perdido zeros à esquerda).</param>
/// <param name="Data">Valor de data quando a célula do Excel é data.</param>
/// <param name="EhPercentual">Célula numérica com formato de porcentagem (o Excel guarda 1,5% como 0,015).</param>
public sealed record CelulaLida(string Texto, decimal? Numero = null, DateTime? Data = null, bool EhPercentual = false)
{
    public static readonly CelulaLida Vazia = new(string.Empty);

    public bool EstaVazia => string.IsNullOrWhiteSpace(Texto) && Numero is null && Data is null;
}

/// <param name="Numero">Número da linha na planilha (o que a pessoa vê no Excel; o cabeçalho é a linha 1).</param>
public sealed record LinhaLida(int Numero, IReadOnlyList<CelulaLida> Celulas)
{
    public CelulaLida Celula(int indice) => indice >= 0 && indice < Celulas.Count ? Celulas[indice] : CelulaLida.Vazia;

    public bool EstaVazia => Celulas.All(c => c.EstaVazia);
}

public sealed record AbaLida(string Nome, IReadOnlyList<string> Cabecalhos, IReadOnlyList<LinhaLida> Linhas);

public sealed record PlanilhaLida(IReadOnlyList<AbaLida> Abas);
