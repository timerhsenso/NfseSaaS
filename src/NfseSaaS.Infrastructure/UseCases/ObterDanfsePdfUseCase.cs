using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Danfse;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Clients;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Tenta o endpoint de PDF da SEFIN Nacional primeiro (caso algum
/// ambiente ainda o mantenha funcionando) e, em qualquer falha, cai pra
/// geração local (DanfsePdfGenerator) a partir do XmlNfse já persistido —
/// necessário desde a descontinuação do endpoint remoto (HTTP 501
/// confirmado em 09/09/2026, NT 008/2026).
/// </summary>
public sealed class ObterDanfsePdfUseCase : IObterDanfsePdfUseCase
{
    private readonly AppDbContext _db;
    private readonly INfseApiClient _apiClient;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public ObterDanfsePdfUseCase(AppDbContext db, INfseApiClient apiClient, IWebHostEnvironment webHostEnvironment)
    {
        _db = db;
        _apiClient = apiClient;
        _webHostEnvironment = webHostEnvironment;
    }

    public async Task<DanfsePdfResponse> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        var nfse = await _db.NotasFiscais.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nfseId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        if (nfse.Status is not (NfseStatus.Autorizada or NfseStatus.Cancelada) || string.IsNullOrWhiteSpace(nfse.ChaveAcesso))
            throw new RegraNegocioException(
                $"Só é possível obter o DANFSe de uma Nfse Autorizada ou Cancelada com ChaveAcesso (status atual: {nfse.Status}).");

        var clienteNome = await _db.Clientes.AsNoTracking()
            .Where(c => c.Id == nfse.ClienteId)
            .Select(c => c.Nome)
            .FirstOrDefaultAsync(cancellationToken) ?? "Cliente";

        var nomeArquivo = MontarNomeArquivo(clienteNome, nfse.NumeroDps, nfse.DataEmissao ?? new DateTimeOffset(nfse.DataCompetencia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        try
        {
            var (statusCode, bytesRemoto, contentType) = await _apiClient.ObterDanfsePdfAsync(nfse.EmpresaId, nfse.TipoAmbiente.ParaTpAmb(), nfse.ChaveAcesso, cancellationToken);

            if (statusCode is >= 200 and < 300 && bytesRemoto.Length > 0)
            {
                return new DanfsePdfResponse(
                    Bytes: bytesRemoto,
                    ContentType: string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
                    NomeArquivo: nomeArquivo);
            }
        }
        catch (NfseApiException)
        {
            // Cai pra geração local abaixo — não é um erro fatal aqui.
        }

        if (string.IsNullOrWhiteSpace(nfse.XmlNfse))
            throw new RegraNegocioException(
                "Não há XML da NFS-e persistido para gerar o DANFSe localmente (nota emitida antes deste recurso existir?).");

        var bytesLocal = DanfsePdfGenerator.Gerar(
            nfse.XmlNfse,
            cancelada: nfse.Status == NfseStatus.Cancelada,
            caminhoLogo: Path.Combine(_webHostEnvironment.WebRootPath, "img", "logo-nfse-horizontal.png"));

        return new DanfsePdfResponse(
            Bytes: bytesLocal,
            ContentType: "application/pdf",
            NomeArquivo: nomeArquivo);
    }

    /// <summary>
    /// {Cliente sem espaço/caractere inválido}_{NumeroDps}_{MêsAno} — o
    /// nome da Empresa (prestador) não entra de propósito: quando várias
    /// notas são baixadas juntas (lote), é a mesma Empresa em todas,
    /// então repetir o nome dela em cada arquivo não ajuda a diferenciar
    /// nada — o Cliente é o que varia nota a nota. NumeroDps garante que
    /// nunca colide (2 notas pro mesmo cliente no mesmo mês viram 2
    /// arquivos diferentes).
    /// </summary>
    internal static string MontarNomeArquivo(string clienteNome, int numeroDps, DateTimeOffset dataEmissao)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var clienteSanitizado = new string(clienteNome.Where(c => c != ' ' && !invalidos.Contains(c)).ToArray());
        if (string.IsNullOrWhiteSpace(clienteSanitizado))
            clienteSanitizado = "Cliente";

        return $"{clienteSanitizado}_{numeroDps}_{dataEmissao:MMyyyy}.pdf";
    }
}
