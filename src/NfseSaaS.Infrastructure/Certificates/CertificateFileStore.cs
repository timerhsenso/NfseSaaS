using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>
/// Lê e grava o certificado .pfx de cada Empresa em disco, criptografado
/// em repouso via ASP.NET Core Data Protection (protegido com DPAPI do
/// Windows — ver AddInfrastructure).
///
/// Cada Empresa tem um arquivo "{empresaId}.cert.json" contendo o .pfx e a
/// senha, cada um protegido separadamente. O arquivo nunca fica no
/// wwwroot nem é versionado.
/// </summary>
public sealed class CertificateFileStore
{
    private const string ProtectorPurpose = "NfseSaaS.Certificados.v1";

    private readonly IDataProtector _protector;
    private readonly CertificateStorageOptions _options;

    public CertificateFileStore(IDataProtectionProvider dataProtectionProvider, IOptions<CertificateStorageOptions> options)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _options = options.Value;
    }

    /// <summary>
    /// Criptografa e grava o .pfx e a senha de uma Empresa. Usado no
    /// cadastro/atualização do certificado (fluxo de UI ainda a
    /// implementar — Fase 2 de casos de uso).
    /// </summary>
    public async Task SalvarAsync(Guid empresaId, byte[] pfxBytes, string senha, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath))
            throw new NfseCertificateException("CertificateStorage:BasePath não configurado.");

        Directory.CreateDirectory(_options.BasePath);

        var envelope = new CertificadoEnvelope(
            PfxProtegidoBase64: Convert.ToBase64String(_protector.Protect(pfxBytes)),
            SenhaProtegida: _protector.Protect(senha));

        var json = JsonSerializer.Serialize(envelope);
        await File.WriteAllTextAsync(CaminhoArquivo(empresaId), json, cancellationToken);
    }

    /// <summary>Lê, descriptografa e carrega o certificado da Empresa informada, já validando chave privada e período de validade.</summary>
    public async Task<X509Certificate2> CarregarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var caminho = CaminhoArquivo(empresaId);

        if (!File.Exists(caminho))
            throw new NfseCertificateException($"Certificado da empresa {empresaId} não encontrado em '{caminho}'.");

        CertificadoEnvelope? envelope;
        try
        {
            var json = await File.ReadAllTextAsync(caminho, cancellationToken);
            envelope = JsonSerializer.Deserialize<CertificadoEnvelope>(json);
        }
        catch (Exception ex)
        {
            throw new NfseCertificateException($"Falha ao ler o arquivo de certificado da empresa {empresaId}.", ex);
        }

        if (envelope is null)
            throw new NfseCertificateException($"Arquivo de certificado da empresa {empresaId} está corrompido.");

        byte[] pfxBytes;
        string senha;
        try
        {
            pfxBytes = _protector.Unprotect(Convert.FromBase64String(envelope.PfxProtegidoBase64));
            senha = _protector.Unprotect(envelope.SenhaProtegida);
        }
        catch (Exception ex)
        {
            throw new NfseCertificateException(
                $"Não foi possível descriptografar o certificado da empresa {empresaId} (chaves de proteção divergentes?).", ex);
        }

        X509Certificate2 certificado;
        try
        {
            // IMPORTANTE: EphemeralKeySet NÃO é usado aqui de propósito.
            // O Windows/SChannel (usado pelo SslStream nativo do .NET no
            // handshake mTLS) precisa que a chave privada esteja acessível
            // via um contêiner de chave persistido para autenticação de
            // cliente — com EphemeralKeySet o handshake falha com
            // "As credenciais fornecidas para o pacote não foram
            // reconhecidas" (SEC_E_UNKNOWN_CREDENTIALS). Esta é exatamente
            // a combinação de flags já validada na POC (emissão real aceita
            // pela SEFIN, HTTP 201).
            certificado = new X509Certificate2(
                pfxBytes,
                senha,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            throw new NfseCertificateException(
                $"Falha ao carregar o certificado da empresa {empresaId} (senha incorreta ou arquivo inválido).", ex);
        }

        if (!certificado.HasPrivateKey)
            throw new NfseCertificateException($"O certificado da empresa {empresaId} não possui chave privada.");

        if (DateTime.Now < certificado.NotBefore || DateTime.Now > certificado.NotAfter)
            throw new NfseCertificateException(
                $"O certificado da empresa {empresaId} está fora do período de validade ({certificado.NotBefore:dd/MM/yyyy} a {certificado.NotAfter:dd/MM/yyyy}).");

        return certificado;
    }

    private string CaminhoArquivo(Guid empresaId) => Path.Combine(_options.BasePath, $"{empresaId:N}.cert.json");

    private sealed record CertificadoEnvelope(string PfxProtegidoBase64, string SenhaProtegida);
}