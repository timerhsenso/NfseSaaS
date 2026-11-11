using System.Text.Json;
using NfseSaaS.Application.UseCases.Nfse;

namespace NfseSaaS.Infrastructure.Contratos;

/// <summary>
/// Lê e grava Nfse.ComposicaoValoresJson (valor de cada serviço do Contrato
/// numa nota). Um lugar só, com as mesmas opções, para gravação e leitura.
/// </summary>
public static class ComposicaoValoresNfse
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    /// <summary>Nulo quando não há composição ou ela não fecha com o total da nota (não grava algo inconsistente).</summary>
    public static string? Serializar(IReadOnlyList<ItemComposicaoNfse>? itens, decimal totalDaNota) =>
        itens is null || itens.Count == 0 || itens.Sum(i => i.Valor) != totalDaNota
            ? null
            : JsonSerializer.Serialize(itens, Opcoes);

    public static IReadOnlyList<ItemComposicaoNfse> Desserializar(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ItemComposicaoNfse>();
        try
        {
            return JsonSerializer.Deserialize<List<ItemComposicaoNfse>>(json, Opcoes) ?? new List<ItemComposicaoNfse>();
        }
        catch (JsonException)
        {
            return Array.Empty<ItemComposicaoNfse>();
        }
    }
}
