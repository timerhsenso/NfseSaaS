namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Convenção de nomes de HttpClient nomeados por Empresa. Usada tanto para
/// obter o client via IHttpClientFactory quanto pelo
/// CertificateHttpMessageHandlerBuilderFilter para saber a qual Empresa
/// anexar o certificado mTLS ao construir o handler.
///
/// Cada Empresa recebe seu PRÓPRIO nome de client (e, portanto, seu próprio
/// pool de conexões dentro do IHttpClientFactory) — isso é proposital:
/// evita que uma conexão TLS já autenticada com o certificado da Empresa A
/// seja reaproveitada numa requisição da Empresa B.
/// </summary>
public static class SefinNacionalClientNames
{
    private const string Prefix = "SefinNacional:";

    public static string ParaEmpresa(Guid empresaId) => Prefix + empresaId.ToString("N");

    public static bool TentarExtrairEmpresaId(string clientName, out Guid empresaId)
    {
        empresaId = Guid.Empty;

        if (!clientName.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        return Guid.TryParse(clientName.AsSpan(Prefix.Length), out empresaId);
    }
}