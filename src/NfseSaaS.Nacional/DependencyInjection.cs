using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
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
/// NÃO registra ICertificateProvider — a implementação real
/// (FileCertificateProvider, certificado criptografado em disco por
/// Empresa) é registrada por NfseSaaS.Infrastructure.AddInfrastructure.
/// Se o Web chamar AddNfseNacional sem antes chamar AddInfrastructure, a
/// falha na resolução de ICertificateProvider é explícita (DI recusa
/// construir o host), não silenciosa.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddNfseNacional(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NfseNacionalOptions>(configuration.GetSection(NfseNacionalOptions.SectionName));

        // Registra a infraestrutura do IHttpClientFactory. Não usamos um
        // nome fixo de client aqui — os clients são criados dinamicamente
        // por Empresa (SefinNacionalClientNames.ParaEmpresa) porque o
        // certificado mTLS depende de qual Empresa está emitindo. Ver
        // CertificateHttpMessageHandlerBuilderFilter para o porquê.
        services.AddHttpClient();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, CertificateHttpMessageHandlerBuilderFilter>();

        services.AddScoped<IDpsValidator, DpsValidator>();
        services.AddScoped<IDpsBuilder, DpsBuilder>();
        services.AddScoped<IDpsSigner, DpsSigner>();
        services.AddScoped<INfseApiClient, NfseApiClient>();
        services.AddScoped<INfseNacionalService, NfseNacionalService>();

        return services;
    }
}