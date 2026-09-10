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

        try
        {
            var (statusCode, bytesRemoto, contentType) = await _apiClient.ObterDanfsePdfAsync(nfse.EmpresaId, nfse.TipoAmbiente.ParaTpAmb(), nfse.ChaveAcesso, cancellationToken);

            if (statusCode is >= 200 and < 300 && bytesRemoto.Length > 0)
            {
                return new DanfsePdfResponse(
                    Bytes: bytesRemoto,
                    ContentType: string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
                    NomeArquivo: $"DANFSe-{nfse.ChaveAcesso}.pdf");
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
            NomeArquivo: $"DANFSe-{nfse.ChaveAcesso}.pdf");
    }
}
