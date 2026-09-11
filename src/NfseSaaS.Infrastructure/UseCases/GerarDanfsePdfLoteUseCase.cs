using System.IO.Compression;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Reaproveita IObterDanfsePdfUseCase (mesmo caminho do download
/// individual — SEFIN primeiro, geração local como fallback) um PDF de
/// cada vez, empacotando tudo num .zip só. Nota que não está
/// Autorizada/Cancelada (ou qualquer outra falha pontual) NÃO aborta o
/// lote — fica de fora do zip e some na lista IdsIgnorados, mesmo
/// raciocínio já usado em EmitirNotaMensalLoteUseCase (uma falha isolada
/// nunca derruba o resto).
/// </summary>
public sealed class GerarDanfsePdfLoteUseCase : IGerarDanfsePdfLoteUseCase
{
    private readonly IObterDanfsePdfUseCase _obterDanfsePdf;

    public GerarDanfsePdfLoteUseCase(IObterDanfsePdfUseCase obterDanfsePdf)
    {
        _obterDanfsePdf = obterDanfsePdf;
    }

    public async Task<DanfsePdfLoteResponse> ExecutarAsync(GerarDanfsePdfLoteRequest request, CancellationToken cancellationToken)
    {
        if (request.NfseIds.Count == 0)
            throw new RegraNegocioException("Selecione ao menos uma nota.");

        var idsIgnorados = new List<Guid>();

        using var memoryStream = new MemoryStream();
        using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nomesUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in request.NfseIds)
            {
                DanfsePdfResponse danfse;
                try
                {
                    danfse = await _obterDanfsePdf.ExecutarAsync(id, cancellationToken);
                }
                catch (Exception ex) when (ex is RegraNegocioException or RecursoNaoEncontradoException)
                {
                    idsIgnorados.Add(id);
                    continue;
                }

                // Dentro do zip, dois arquivos nunca podem ter o mesmo
                // nome — MontarNomeArquivo não garante unicidade global
                // (só por cliente+número+mês), então desempata aqui se
                // precisar.
                var nomeFinal = danfse.NomeArquivo;
                var contador = 2;
                while (!nomesUsados.Add(nomeFinal))
                {
                    var semExtensao = Path.GetFileNameWithoutExtension(danfse.NomeArquivo);
                    nomeFinal = $"{semExtensao} ({contador}).pdf";
                    contador++;
                }

                var entrada = zip.CreateEntry(nomeFinal, CompressionLevel.Fastest);
                await using var entradaStream = entrada.Open();
                await entradaStream.WriteAsync(danfse.Bytes, cancellationToken);
            }
        }

        if (memoryStream.Length == 0 || idsIgnorados.Count == request.NfseIds.Count)
            throw new RegraNegocioException("Nenhuma das notas selecionadas tem DANFSe disponível (precisam estar Autorizada ou Cancelada).");

        return new DanfsePdfLoteResponse(
            ZipBytes: memoryStream.ToArray(),
            NomeArquivoZip: $"DANFSe-lote-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip",
            IdsIgnorados: idsIgnorados);
    }
}
