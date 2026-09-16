using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NfseSaaS.Nacional.Builders;
using NfseSaaS.Nacional.Clients;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Resilience;
using NfseSaaS.Nacional.Services;
using NfseSaaS.Nacional.Signing;
using NfseSaaS.Nacional.Validation;
using Polly;
using Polly.Wrap;

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
        services.Configure<AdnOptions>(configuration.GetSection(AdnOptions.SectionName));

        // Registra a infraestrutura do IHttpClientFactory. Não usamos um
        // nome fixo de client aqui — os clients são criados dinamicamente
        // por Empresa (SefinNacionalClientNames.ParaEmpresa) porque o
        // certificado mTLS depende de qual Empresa está emitindo. Ver
        // CertificateHttpMessageHandlerBuilderFilter para o porquê.
        services.AddHttpClient();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, CertificateHttpMessageHandlerBuilderFilter>();

        // Resiliência (retry + circuit breaker) pra TODO HttpClient criado
        // pela factory — cobre os clients dinâmicos por Empresa acima e,
        // como ConfigureHttpClientDefaults é global ao container de DI,
        // também acaba cobrindo o client nomeado "BrasilApi" registrado
        // em NfseSaaS.Infrastructure (consulta de CNPJ) — bônus aceitável,
        // já que é outra chamada HTTP externa que também se beneficia de
        // retry em falha transitória. Instância única e reutilizada de
        // propósito: o circuit breaker precisa manter estado (contagem de
        // falhas/tempo aberto) entre chamadas — criar uma política nova a
        // cada requisição nunca abriria o circuito de verdade.
        var resilienciaHttp = PollyPolicies.CriarCircuitBreaker().WrapAsync(PollyPolicies.CriarRetry());
        services.ConfigureHttpClientDefaults(http => http.AddPolicyHandler(resilienciaHttp));

        services.AddScoped<IDpsValidator, DpsValidator>();
        services.AddScoped<IDpsBuilder, DpsBuilder>();
        services.AddScoped<IDpsSigner, DpsSigner>();
        services.AddScoped<IEventoCancelamentoBuilder, EventoCancelamentoBuilder>();
        services.AddScoped<INfseApiClient, NfseApiClient>();
        services.AddScoped<IAdnDistribuicaoClient, AdnDistribuicaoClient>();
        services.AddScoped<INfseNacionalService, NfseNacionalService>();

        return services;
    }
}
