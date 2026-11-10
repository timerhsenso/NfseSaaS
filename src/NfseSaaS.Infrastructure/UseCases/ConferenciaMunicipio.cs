using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Municipios;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// C9: ao salvar Cliente ou Empresa, o código do município precisa existir
/// na tabela do IBGE (senão a SEFIN rejeita a nota) e a UF passa a ser a do
/// município — "2927408" com UF "SP" não chega mais à DPS.
/// </summary>
internal static class ConferenciaMunicipio
{
    /// <returns>A UF do município, ou a UF informada quando o código está em branco.</returns>
    public static async Task<string> UfConferidaAsync(AppDbContext db, string? codigoMunicipio, string? ufInformada, CancellationToken cancellationToken)
    {
        var codigo = (codigoMunicipio ?? string.Empty).Trim();
        if (codigo.Length == 0)
            return (ufInformada ?? string.Empty).Trim().ToUpperInvariant();

        // Tabela ainda vazia (startup sem o seeder, ex.: ferramenta de linha de comando): não trava o cadastro.
        if (!await db.Municipios.AnyAsync(cancellationToken))
            return (ufInformada ?? string.Empty).Trim().ToUpperInvariant();

        var uf = await db.Municipios.AsNoTracking().Where(m => m.Codigo == codigo).Select(m => m.Uf).FirstOrDefaultAsync(cancellationToken);
        return uf ?? throw new RegraNegocioException(RegrasMunicipio.MensagemNaoExiste(codigo));
    }
}
