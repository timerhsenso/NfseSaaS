using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NfseSaaS.Nacional.Abstractions;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Anexa o certificado digital A1 correto (mTLS) ao handler do HttpClient
/// de cada Empresa.
///
/// DECISÃO DE ARQUITETURA (fecha o ponto em aberto do requisito 13/11 da
/// especificação — "certificado por Empresa"):
///
/// No SaaS, cada Empresa possui seu próprio certificado. Um único
/// HttpClient nomeado fixo (ex.: "SefinNacional") registrado uma vez no
/// startup NÃO FUNCIONA, porque o certificado a anexar depende de qual
/// Empresa está emitindo naquela chamada — e o handler é criado/(re)usado
/// pelo IHttpClientFactory de forma compartilhada e independente de cada
/// requisição.
///
/// A alternativa de um único client compartilhado com
/// SslOptions.LocalCertificateSelectionCallback dinâmico foi considerada e
/// REJEITADA: como o IHttpClientFactory reaproveita conexões TLS já
/// estabelecidas dentro do pool de um mesmo nome de client, uma conexão
/// autenticada com o certificado da Empresa A poderia ser reutilizada numa
/// requisição da Empresa B — um vazamento real de isolamento entre
/// empresas/tenants, inaceitável num sistema fiscal.
///
/// Estratégia adotada: HttpClient nomeado DINAMICAMENTE por Empresa
/// (ver SefinNacionalClientNames.ParaEmpresa), cada nome com seu próprio
/// handler e pool de conexões isolado. Este filtro intercepta a construção
/// de QUALQUER client cujo nome siga essa convenção e anexa o certificado
/// correspondente àquela Empresa.
/// </summary>
public sealed class CertificateHttpMessageHandlerBuilderFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly IServiceProvider _serviceProvider;

    public CertificateHttpMessageHandlerBuilderFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            next(builder);

            if (!SefinNacionalClientNames.TentarExtrairEmpresaId(builder.Name, out var empresaId))
                return;

            builder.PrimaryHandler = CriarHandlerComCertificado(empresaId);
        };
    }

    private HttpClientHandler CriarHandlerComCertificado(Guid empresaId)
    {
        // Escopo próprio e deliberado: este método é chamado pelo próprio
        // IHttpClientFactory na criação/renovação do handler (conforme
        // PooledConnectionLifetime), fora do ciclo de vida de uma
        // requisição HTTP específica — por isso não pode depender de um
        // serviço Scoped de uma requisição em andamento.
        using var scope = _serviceProvider.CreateScope();
        var certificateProvider = scope.ServiceProvider.GetRequiredService<ICertificateProvider>();

        X509Certificate2 certificado;
        try
        {
            // Sync-over-async deliberado: IHttpMessageHandlerBuilderFilter
            // não expõe uma API assíncrona. É seguro aqui porque ASP.NET
            // Core não usa SynchronizationContext (sem risco de deadlock) e
            // a chamada é rara — só ocorre na criação/renovação do handler,
            // não a cada requisição.
            certificado = certificateProvider
                .ObterCertificadoAsync(empresaId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex) when (ex is not NfseCertificateException)
        {
            throw new NfseCertificateException(
                $"Não foi possível obter o certificado digital da empresa {empresaId} para configurar o mTLS.", ex);
        }

        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            CheckCertificateRevocationList = true
        };
        handler.ClientCertificates.Add(certificado);
        return handler;
    }
}