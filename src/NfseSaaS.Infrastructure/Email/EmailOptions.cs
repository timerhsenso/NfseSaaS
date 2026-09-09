namespace NfseSaaS.Infrastructure.Email;

/// <summary>Configuração de SMTP — seção "Email" do appsettings/user-secrets.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UsarSsl { get; set; } = true;
    public string Usuario { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
    public string RemetenteEmail { get; set; } = string.Empty;
    public string RemetenteNome { get; set; } = "NfseSaaS";

    /// <summary>
    /// URL base do frontend, usada para montar o link de aceite de convite
    /// (ex.: "https://app.suaempresa.com.br"). Hoje não existe uma página
    /// real nesse endereço (MVC/Razor ainda não construído) — por isso o
    /// e-mail SEMPRE também traz o token em texto puro como alternativa,
    /// utilizável via Swagger/Postman enquanto o frontend não existe. Deixe
    /// em branco (padrão) para o e-mail sair sem o link, só com o token.
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;
}
