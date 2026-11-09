using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// ZIP com o XML de cada nota autorizada ou cancelada da empresa no mês de
/// competência. Nome de cada arquivo igual ao do download individual
/// (ObterXmlNfseUseCase). O filtro de conta do DbContext garante que só
/// a própria conta baixa (empresa de outra conta = não encontrada).
/// </summary>
public sealed class BaixarXmlMesUseCase : IBaixarXmlMesUseCase
{
    private const int Lote = 200;
    private static readonly TimeSpan OffsetBrasilia = TimeSpan.FromHours(-3);

    private readonly AppDbContext _db;

    public BaixarXmlMesUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<XmlMesResponse> ExecutarAsync(Guid empresaId, DateOnly competencia, TipoAmbiente? ambiente, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.AsNoTracking()
            .Where(e => e.Id == empresaId)
            .Select(e => new { e.Cnpj, e.TipoAmbiente })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var amb = ambiente ?? empresa.TipoAmbiente;
        var inicio = new DateOnly(competencia.Year, competencia.Month, 1);
        var fim = inicio.AddMonths(1);

        var consulta = _db.NotasFiscais.AsNoTracking()
            .Where(n => n.EmpresaId == empresaId
                        && n.TipoAmbiente == amb
                        && (n.Status == NfseStatus.Autorizada || n.Status == NfseStatus.Cancelada)
                        && n.XmlNfse != null
                        && n.DataCompetencia >= inicio && n.DataCompetencia < fim);

        var total = await consulta.CountAsync(cancellationToken);
        if (total == 0)
            throw new RegraNegocioException($"Nenhuma nota autorizada ou cancelada em {inicio:MM/yyyy}{(amb == TipoAmbiente.Producao ? "" : " (homologação)")}.");
        if (total > RegrasXmlMes.MaximoNotas)
            throw new RegraNegocioException($"O mês tem {total} notas, acima do limite de {RegrasXmlMes.MaximoNotas} por arquivo. Use a exportação completa em Meus dados.");

        var apelidos = await _db.Clientes.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => new { c.Id, c.Apelido })
            .ToDictionaryAsync(c => c.Id, c => c.Apelido, cancellationToken);

        var nomesUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var pagina = 0; pagina * Lote < total; pagina++)
            {
                var notas = await consulta
                    .OrderBy(n => n.NumeroDps).ThenBy(n => n.Id)
                    .Skip(pagina * Lote).Take(Lote)
                    .Select(n => new { n.XmlNfse, n.NumeroDps, n.DataEmissao, n.DataCompetencia, n.ClienteId })
                    .ToListAsync(cancellationToken);

                foreach (var n in notas)
                {
                    var data = n.DataEmissao is { } emissao
                        ? DateOnly.FromDateTime(emissao.ToOffset(OffsetBrasilia).DateTime)
                        : n.DataCompetencia;
                    var baseNome = Path.GetFileNameWithoutExtension(
                        ObterDanfsePdfUseCase.MontarNomeArquivo(apelidos.GetValueOrDefault(n.ClienteId, "Cliente"), n.NumeroDps, data));
                    var nome = baseNome + ".xml";
                    for (var i = 2; !nomesUsados.Add(nome); i++)
                        nome = $"{baseNome} ({i}).xml";

                    var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal);
                    await using var s = entrada.Open();
                    await s.WriteAsync(Encoding.UTF8.GetBytes(n.XmlNfse!), cancellationToken);
                }
            }
        }

        return new XmlMesResponse(memoria.ToArray(), RegrasXmlMes.NomeArquivo(empresa.Cnpj, inicio, amb), total);
    }
}
