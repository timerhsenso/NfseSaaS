namespace NfseSaaS.Application.UseCases.Certificados;

/// <summary>
/// Status do certificado digital de uma Empresa. Nunca inclui o .pfx nem
/// a senha — só metadados públicos do próprio certificado (o Subject de
/// um certificado X.509 não é segredo, é informação de identificação).
/// </summary>
public sealed record CertificadoStatusResponse(
    bool Existe,
    string? Subject,
    DateTimeOffset? ValidoDe,
    DateTimeOffset? ValidoAte,
    bool Valido);
