namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>Seção "Cobranca" do appsettings (ou Cobranca__Ativa etc. no compose).</summary>
public sealed class CobrancaOptions
{
    public const string SectionName = "Cobranca";

    /// <summary>
    /// false (padrão) = beta: ninguém é bloqueado e o teste grátis nem começa
    /// a contar. Ligar só quando os planos estiverem valendo.
    /// </summary>
    public bool Ativa { get; set; }

    /// <summary>Contas que nunca são cobradas (ex.: a conta do próprio operador).</summary>
    public List<Guid> ContasIsentas { get; set; } = new();
}

/// <summary>Seção "Asaas". A chave de API e o token do webhook nunca vão para o Git (user-secrets / compose da VPS).</summary>
public sealed class AsaasOptions
{
    public const string SectionName = "Asaas";

    /// <summary>Sandbox: https://api-sandbox.asaas.com/v3 · Produção: https://api.asaas.com/v3</summary>
    public string BaseUrl { get; set; } = "https://api-sandbox.asaas.com/v3";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Token que o Asaas envia no header "asaas-access-token" do webhook (32 a 255 caracteres).</summary>
    public string WebhookToken { get; set; } = string.Empty;

    /// <summary>O Asaas exige User-Agent em toda chamada.</summary>
    public string UserAgent { get; set; } = "micoempresa";
}
