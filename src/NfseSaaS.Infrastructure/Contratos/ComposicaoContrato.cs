using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Domain.Snapshots;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Contratos;

/// <summary>
/// Monta e lê a "fotografia" das linhas de serviço gravada em cada evento
/// do Histórico de valores (MovimentacaoValorContrato.ComposicaoJson).
/// Um lugar só pra serializar e desserializar, com as mesmas opções, pra
/// gravação e leitura nunca divergirem.
/// </summary>
public static class ComposicaoContrato
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializa as linhas informadas com a descrição ATUAL de cada Serviço.
    /// Recebe as linhas em memória (não relê ContratoServicos) porque é
    /// chamado antes do SaveChanges, com linhas ainda não gravadas.
    /// </summary>
    public static async Task<string> SerializarAsync(
        AppDbContext db,
        IEnumerable<(Guid ServicoId, decimal Quantidade, decimal ValorUnitario)> linhas,
        CancellationToken cancellationToken)
    {
        var lista = linhas.ToList();
        var ids = lista.Select(l => l.ServicoId).Distinct().ToList();

        var descricoes = await db.Servicos.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Descricao, cancellationToken);

        var composicao = lista
            .Select(l => new ComposicaoContratoLinha(
                l.ServicoId,
                descricoes.GetValueOrDefault(l.ServicoId, "Serviço removido"),
                l.Quantidade,
                l.ValorUnitario))
            .ToList();

        return JsonSerializer.Serialize(composicao, Opcoes);
    }

    /// <summary>Lê a composição gravada; nulo/ilegível = lista vazia (eventos antigos, migrados sem composição).</summary>
    public static IReadOnlyList<ComposicaoContratoLinha> Desserializar(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ComposicaoContratoLinha>();

        try
        {
            return JsonSerializer.Deserialize<List<ComposicaoContratoLinha>>(json, Opcoes) ?? new List<ComposicaoContratoLinha>();
        }
        catch (JsonException)
        {
            return Array.Empty<ComposicaoContratoLinha>();
        }
    }
}
