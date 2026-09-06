using NfseSaaS.Nacional.Abstractions;
using NfseSaaS.Nacional.Builders;
using NfseSaaS.Nacional.Clients;
using NfseSaaS.Nacional.Exceptions;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Responses;
using NfseSaaS.Nacional.Signing;
using NfseSaaS.Nacional.Validation;

namespace NfseSaaS.Nacional.Services;

/// <summary>
/// Orquestra o fluxo completo de emissão: valida → constrói o XML da DPS →
/// assina digitalmente → compacta em GZip+Base64 → envia à SEFIN → interpreta
/// a resposta. A orquestração em si já reflete o fluxo comprovado na POC;
/// os passos de build/assinatura (<see cref="IDpsBuilder"/>, <see cref="IDpsSigner"/>)
/// permanecem como placeholder até a migração da Fase 2 (ver comentários
/// nas respectivas classes).
/// </summary>
public sealed class NfseNacionalService : INfseNacionalService
{
    private readonly IDpsValidator _validator;
    private readonly IDpsBuilder _builder;
    private readonly IDpsSigner _signer;
    private readonly ICertificateProvider _certificateProvider;
    private readonly INfseApiClient _apiClient;

    public NfseNacionalService(
        IDpsValidator validator,
        IDpsBuilder builder,
        IDpsSigner signer,
        ICertificateProvider certificateProvider,
        INfseApiClient apiClient)
    {
        _validator = validator;
        _builder = builder;
        _signer = signer;
        _certificateProvider = certificateProvider;
        _apiClient = apiClient;
    }

    public async Task<NfseNacionalResponse> EmitirAsync(DpsRequest request, Guid empresaId, CancellationToken cancellationToken)
    {
        var erros = _validator.Validar(request);
        if (erros.Count > 0)
            throw new NfseValidationException("DPS reprovada em validação local.", erros.ToArray());

        var certificado = await _certificateProvider.ObterCertificadoAsync(empresaId, cancellationToken);

        var (xmlDps, infDpsId) = _builder.Construir(request);
        var xmlAssinado = _signer.Assinar(xmlDps, infDpsId, certificado);
        var gzipBase64 = GZipHelper.ComprimirParaBase64(xmlAssinado);

        var (statusCode, body) = await _apiClient.EnviarDpsAsync(empresaId, gzipBase64, cancellationToken);

        return NfseResponseParser.Parse(statusCode, body);
    }
}
