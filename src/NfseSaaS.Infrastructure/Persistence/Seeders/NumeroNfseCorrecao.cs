using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Infrastructure.SincronizacaoSefin;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Correção de dado das notas emitidas antes de 11/2026: o campo NumeroNfse
/// recebia o Id da DPS ("DPS" + 42 dígitos) em vez do número oficial da
/// NFS-e (nNFSe, gerado pela SEFIN e presente no XML autorizado).
/// - Nota com XML autorizado: NumeroNfse = nNFSe do XML.
/// - Nota sem XML (rejeitada, em processamento): o Id da DPS sai do campo
///   (nota sem NFS-e não tem número de NFS-e).
/// Idempotente: só olha notas cujo NumeroNfse ainda começa com "DPS" ou está
/// vazio com XML presente — depois da primeira subida não encontra mais nada.
/// Grava com ExecuteUpdate (uma nota por vez, sem passar pelo filtro de
/// tenant, porque roda fora de requisição) — mesmo espírito do GrupoBackfillSeeder.
/// </summary>
public static class NumeroNfseCorrecao
{
    public static async Task CorrigirAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NumeroNfseCorrecao));

        var candidatas = await db.NotasFiscais.IgnoreQueryFilters().AsNoTracking()
            .Where(n => (n.NumeroNfse != null && n.NumeroNfse.StartsWith("DPS"))
                        || (n.NumeroNfse == null && n.XmlNfse != null))
            .Select(n => new { n.Id, n.XmlNfse })
            .ToListAsync();

        if (candidatas.Count == 0)
            return;

        var corrigidas = 0;
        foreach (var nota in candidatas)
        {
            string? numero = null;
            if (!string.IsNullOrWhiteSpace(nota.XmlNfse))
            {
                try
                {
                    numero = new NfseXmlImportDados(nota.XmlNfse).NumeroNfse();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "XML da nota {NfseId} não pôde ser lido para obter o número da NFS-e.", nota.Id);
                    continue;
                }
            }

            await db.NotasFiscais.IgnoreQueryFilters()
                .Where(n => n.Id == nota.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.NumeroNfse, string.IsNullOrWhiteSpace(numero) ? null : numero));
            corrigidas++;
        }

        logger.LogInformation("Número da NFS-e corrigido em {Quantidade} nota(s) (antes guardava o Id da DPS).", corrigidas);
    }
}
