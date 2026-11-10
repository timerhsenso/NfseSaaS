using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.UseCases.Municipios;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Tabela de municípios do IBGE (C9), embarcada em Dados/municipios.json
/// (5.571 municípios; fonte: códigos do IBGE, compilados em
/// github.com/kelvins/municipios-brasileiros, licença MIT). Roda no startup:
/// inclui o que falta e atualiza nome/UF que mudaram (município muda de
/// nome às vezes; nenhuma nota guarda o nome daqui). Nunca apaga.
/// </summary>
public static class MunicipioSeeder
{
    private sealed record Item(
        [property: JsonPropertyName("codigo")] string Codigo,
        [property: JsonPropertyName("nome")] string Nome,
        [property: JsonPropertyName("uf")] string Uf);

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existentes = await db.Municipios.ToDictionaryAsync(m => m.Codigo);
        var mudou = false;
        foreach (var item in Carregar())
        {
            var nomeBusca = RegrasMunicipio.NormalizarBusca(item.Nome);
            if (!existentes.TryGetValue(item.Codigo, out var atual))
            {
                db.Municipios.Add(new Municipio { Codigo = item.Codigo, Nome = item.Nome, Uf = item.Uf, NomeBusca = nomeBusca });
                mudou = true;
            }
            else if (atual.Nome != item.Nome || atual.Uf != item.Uf || atual.NomeBusca != nomeBusca)
            {
                atual.Nome = item.Nome;
                atual.Uf = item.Uf;
                atual.NomeBusca = nomeBusca;
                mudou = true;
            }
        }

        if (mudou)
            await db.SaveChangesAsync();
    }

    internal static List<(string Codigo, string Nome, string Uf)> CarregarTabela() =>
        Carregar().Select(i => (i.Codigo, i.Nome, i.Uf)).ToList();

    private static List<Item> Carregar()
    {
        const string recurso = "NfseSaaS.Infrastructure.Persistence.Seeders.Dados.municipios.json";
        using var stream = typeof(MunicipioSeeder).Assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"Recurso embarcado '{recurso}' não encontrado — confirme o <EmbeddedResource> no csproj.");
        return JsonSerializer.Deserialize<List<Item>>(stream)
            ?? throw new InvalidOperationException("municipios.json embarcado veio vazio/inválido.");
    }
}
