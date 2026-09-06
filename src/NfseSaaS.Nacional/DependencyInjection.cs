using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Nacional.Builders;
using NfseSaaS.Nacional.Clients;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Services;
using NfseSaaS.Nacional.Signing;
using NfseSaaS.Nacional.Validation;

namespace NfseSaaS.Nacional;

/// <summary>
/// Ponto único de registro do módulo NfseSaaS.Nacional no container de DI.
/// Mantém o Program.cs do Web enxuto (ver Requisito 16 da especificação).
///
/// NÃO registra ICertificateProvider — a implementação concreta depende de
/// onde o certificado será armazenado (decisão de fase futura) e deve ser
/// registrada pela camada que a implementar.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddNfseNacional(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NfseNacionalOptions>(configuration.GetSection(NfseNacionalOptions.SectionName));

        var options = configuration.GetSection(NfseNacionalOptions.SectionName).Get<NfseNacionalOptions>()
            ?? new NfseNacionalOptions();

        services.AddHttpClient(NfseApiClient.HttpClientName, client =>
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                client.BaseAddress = new Uri(options.BaseUrl);

            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddScoped<IDpsValidator, DpsValidator>();
        services.AddScoped<IDpsBuilder, DpsBuilder>();
        services.AddScoped<IDpsSigner, DpsSigner>();
        services.AddScoped<INfseApiClient, NfseApiClient>();
        services.AddScoped<INfseNacionalService, NfseNacionalService>();

        return services;
    }
}
