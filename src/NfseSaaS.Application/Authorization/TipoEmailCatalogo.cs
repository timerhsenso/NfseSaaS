namespace NfseSaaS.Application.Authorization;

/// <summary>Códigos estáveis dos tipos de e-mail — ver EmailLog.Tipo.</summary>
public static class TipoEmailCatalogo
{
    public const string Convite = "Convite";
    public const string ResetSenha = "ResetSenha";
    public const string EsqueciSenha = "EsqueciSenha";
    public const string AvisoTrocaSenha = "AvisoTrocaSenha";

    /// <summary>Nota autorizada (PDF e XML anexos) enviada ao tomador.</summary>
    public const string NfseTomador = "NfseTomador";

    /// <summary>Certificado digital da Empresa vencendo (30/15/7 dias) ou vencido.</summary>
    public const string CertificadoVencendo = "CertificadoVencendo";
}
