using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Garante que o catálogo oficial de Código de Tributação Nacional
/// (cTribNac) existe no banco — idempotente, mesmo padrão do TelaSeeder.
/// Roda uma vez no startup.
///
/// Os dados vêm de Dados/ctribnac.json, embarcado no assembly (não lido
/// de disco em runtime — funciona igual em qualquer forma de deploy,
/// Docker ou IIS). É a tabela oficial de 338 códigos publicada pela
/// Receita Federal/Comitê Gestor da NFS-e (gov.br/nfse, Biblioteca >
/// Documentação Técnica), derivada da lista de serviços da LC 116/2003.
///
/// Só ADICIONA códigos que ainda não existem — nunca atualiza descrição
/// de um código já salvo (mesmo raciocínio do TelaSeeder: se o governo
/// revisar o texto de um código existente, isso entra manualmente, pra
/// não sobrescrever silenciosamente algo que uma Nfse já emitida possa
/// ter referenciado com o texto antigo).
/// </summary>
public static class CodigoTributacaoNacionalSeeder
{
    private sealed record ItemCTribNac(
        [property: JsonPropertyName("codigo")] string Codigo,
        [property: JsonPropertyName("descricao")] string Descricao);

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var codigosExistentes = (await db.CodigosTributacaoNacional
            .Select(c => c.Codigo)
            .ToListAsync())
            .ToHashSet();

        foreach (var item in CarregarDoAssembly())
        {
            if (codigosExistentes.Contains(item.Codigo))
                continue;

            db.CodigosTributacaoNacional.Add(new CodigoTributacaoNacional
            {
                Codigo = item.Codigo,
                Descricao = item.Descricao
            });
        }

        await db.SaveChangesAsync();
    }

    private static List<ItemCTribNac> CarregarDoAssembly()
    {
        var assembly = typeof(CodigoTributacaoNacionalSeeder).Assembly;
        const string recurso = "NfseSaaS.Infrastructure.Persistence.Seeders.Dados.ctribnac.json";

        using var stream = assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException(
                $"Recurso embarcado '{recurso}' não encontrado — confirme o <EmbeddedResource> no csproj.");

        return JsonSerializer.Deserialize<List<ItemCTribNac>>(stream)
            ?? throw new InvalidOperationException("ctribnac.json embarcado veio vazio/inválido.");
    }
}
