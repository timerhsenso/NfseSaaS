using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Garante que o catálogo oficial de NBS (Nomenclatura Brasileira de
/// Serviços) existe no banco — idempotente, mesmo padrão do
/// CodigoTributacaoNacionalSeeder e do TelaSeeder. Roda uma vez no
/// startup.
///
/// Dados de Dados/nbs.json, embarcado no assembly — 920 códigos-FOLHA
/// (9 dígitos) extraídos da tabela oficial NBS 2.0 (gov.br/mdic),
/// excluindo as linhas de agrupamento hierárquico (menos de 9 dígitos),
/// que não são valores válidos pra submeter numa DPS. Ver
/// CodigoNbs para o raciocínio completo.
/// </summary>
public static class CodigoNbsSeeder
{
    private sealed record ItemNbs(
        [property: JsonPropertyName("codigo")] string Codigo,
        [property: JsonPropertyName("descricao")] string Descricao);

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var codigosExistentes = (await db.CodigosNbs
            .Select(c => c.Codigo)
            .ToListAsync())
            .ToHashSet();

        foreach (var item in CarregarDoAssembly())
        {
            if (codigosExistentes.Contains(item.Codigo))
                continue;

            db.CodigosNbs.Add(new CodigoNbs
            {
                Codigo = item.Codigo,
                Descricao = item.Descricao
            });
        }

        await db.SaveChangesAsync();
    }

    private static List<ItemNbs> CarregarDoAssembly()
    {
        var assembly = typeof(CodigoNbsSeeder).Assembly;
        const string recurso = "NfseSaaS.Infrastructure.Persistence.Seeders.Dados.nbs.json";

        using var stream = assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException(
                $"Recurso embarcado '{recurso}' não encontrado — confirme o <EmbeddedResource> no csproj.");

        return JsonSerializer.Deserialize<List<ItemNbs>>(stream)
            ?? throw new InvalidOperationException("nbs.json embarcado veio vazio/inválido.");
    }
}
