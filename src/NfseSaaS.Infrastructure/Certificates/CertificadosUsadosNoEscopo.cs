using System.Security.Cryptography.X509Certificates;

namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>
/// SEG1 — qual certificado A1 foi aberto nesta requisição/job (por Empresa),
/// para a auditoria da emissão e do cancelamento dizer qual certificado
/// assinou (impressão digital SHA-1, a mesma que o Windows mostra). Scoped:
/// o FileCertificateProvider anota ao carregar; os use cases leem depois de
/// chamar a SEFIN. Não guarda o certificado, só os dados públicos.
/// </summary>
public sealed class CertificadosUsadosNoEscopo
{
    private readonly Dictionary<Guid, CertificadoUsado> _usados = new();

    public void Registrar(Guid empresaId, X509Certificate2 certificado) =>
        _usados[empresaId] = new CertificadoUsado(certificado.Thumbprint, certificado.Subject, certificado.NotAfter);

    public CertificadoUsado? Obter(Guid empresaId) => _usados.GetValueOrDefault(empresaId);
}

public sealed record CertificadoUsado(string Thumbprint, string Titular, DateTime ValidoAte);
