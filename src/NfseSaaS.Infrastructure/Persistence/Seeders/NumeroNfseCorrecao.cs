using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Infrastructure.SincronizacaoSefin;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Correção de dado: o campo NumeroNfse tem de guardar o número oficial
/// curto da NFS-e (nNFSe, ex.: "44"). Notas antigas guardaram o Id da DPS
/// ("DPS" + 42 dígitos) ou o Id da nota ("NFS" + chave de 50 dígitos), que
/// aparecia enorme na lista de notas.
/// Ordem para achar o número certo:
///   1. nNFSe do XML autorizado (ou o número dentro do Id do XML);
///   2. número dentro da chave de acesso gravada;
///   3. número dentro do próprio valor errado ("NFS…");
///   4. nada encontrado → null (nota sem NFS-e não tem número de NFS-e).
/// Idempotente: só olha notas cujo NumeroNfse ainda não é um número limpo
/// (começa com letra ou zero, é longo demais ou está vazio com XML) —
/// depois da primeira subida não altera mais nada.
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

        // Filtro largo no banco; a decisão fina (PrecisaCorrigir) é feita em memória.
        var candidatas = (await db.NotasFiscais.IgnoreQueryFilters().AsNoTracking()
                .Where(n => (n.NumeroNfse != null
                             && (n.NumeroNfse.StartsWith("DPS") || n.NumeroNfse.StartsWith("NFS")
                                 || n.NumeroNfse.StartsWith("0") || n.NumeroNfse.Length > 15))
                            || (n.NumeroNfse == null && n.XmlNfse != null))
                .Select(n => new { n.Id, n.NumeroNfse, n.XmlNfse, n.ChaveAcesso })
                .ToListAsync())
            .Where(n => n.NumeroNfse is null || NumeroNfseOficial.PrecisaCorrigir(n.NumeroNfse))
            .ToList();

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
                }
            }

            numero ??= NumeroNfseOficial.DaChave(nota.ChaveAcesso) ?? NumeroNfseOficial.Normalizar(nota.NumeroNfse);

            if (numero == nota.NumeroNfse)
                continue;

            await db.NotasFiscais.IgnoreQueryFilters()
                .Where(n => n.Id == nota.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.NumeroNfse, numero));
            corrigidas++;
        }

        if (corrigidas > 0)
            logger.LogInformation("Número da NFS-e corrigido em {Quantidade} nota(s) (guardava o Id da DPS ou da nota).", corrigidas);
    }
}
