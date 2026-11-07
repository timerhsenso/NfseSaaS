namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>
/// Cópias do e-mail da nota (Cliente.EmailsCopia): até 5 endereços além do
/// e-mail principal, gravados numa coluna só, separados por ";". Cc (todos
/// veem quem recebeu) ou Cco (cópia oculta), conforme Cliente.CopiaOculta.
/// </summary>
public static class EmailsCopiaCliente
{
    public const int Maximo = 5;
    public const int TamanhoMaximoEndereco = 100;

    /// <summary>Coluna no banco: 5 × 100 + separadores.</summary>
    public const int TamanhoMaximoColuna = Maximo * (TamanhoMaximoEndereco + 1);

    private const char Separador = ';';

    /// <summary>
    /// Endereços aparados, sem vazios e sem repetidos (sem diferenciar
    /// maiúsculas), na ordem em que vieram. Não valida o formato — isso é
    /// do validator.
    /// </summary>
    public static IReadOnlyList<string> Normalizar(IEnumerable<string?>? enderecos) =>
        (enderecos ?? Array.Empty<string?>())
            .Select(e => e?.Trim() ?? string.Empty)
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Lista → coluna (null quando não há cópia).</summary>
    public static string? Juntar(IEnumerable<string?>? enderecos)
    {
        var lista = Normalizar(enderecos);
        return lista.Count == 0 ? null : string.Join(Separador, lista);
    }

    /// <summary>Coluna → lista (vazia quando não há cópia).</summary>
    public static IReadOnlyList<string> Separar(string? coluna) =>
        string.IsNullOrWhiteSpace(coluna)
            ? Array.Empty<string>()
            : Normalizar(coluna.Split(Separador));
}
