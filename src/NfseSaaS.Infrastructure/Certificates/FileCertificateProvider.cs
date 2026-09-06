using System.Security.Cryptography.X509Certificates;
using NfseSaaS.Nacional.Abstractions;

namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>
/// Implementação real de ICertificateProvider: carrega o certificado A1 de
/// cada Empresa a partir de um arquivo criptografado em disco (ver
/// CertificateFileStore). Substitui o placeholder que existia em
/// NfseSaaS.Nacional (ver DependencyInjection do módulo Nacional).
/// </summary>
public sealed class FileCertificateProvider : ICertificateProvider
{
    private readonly CertificateFileStore _store;

    public FileCertificateProvider(CertificateFileStore store)
    {
        _store = store;
    }

    public Task<X509Certificate2> ObterCertificadoAsync(Guid empresaId, CancellationToken cancellationToken) =>
        _store.CarregarAsync(empresaId, cancellationToken);
}