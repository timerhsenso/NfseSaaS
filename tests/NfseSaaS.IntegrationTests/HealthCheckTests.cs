using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Placeholder de teste de integração — não chama a SEFIN (Requisito 25).
/// Sobe a aplicação em memória via WebApplicationFactory e confere que o
/// endpoint /health responde. Requer PostgreSQL acessível (docker-compose)
/// para retornar Healthy; ainda assim, o endpoint deve responder mesmo que
/// o banco esteja fora do ar (retornará Unhealthy, não erro 500).
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class HealthCheckTests
{
    private readonly AplicacaoTesteFactory _factory;

    public HealthCheckTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Endpoint_health_deve_responder()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.True(response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.ServiceUnavailable);
    }
}
