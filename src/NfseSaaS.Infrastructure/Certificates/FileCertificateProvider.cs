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
    private readonly CertificadosUsadosNoEscopo? _usados;

    public FileCertificateProvider(CertificateFileStore store, CertificadosUsadosNoEscopo? usados = null)
    {
        _store = store;
        _usados = usados;
    }

    public async Task<X509Certificate2> ObterCertificadoAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var certificado = await _store.CarregarAsync(empresaId, cancellationToken);
        _usados?.Registrar(empresaId, certificado); // auditoria: qual certificado assinou (SEG1)
        return certificado;
    }
}