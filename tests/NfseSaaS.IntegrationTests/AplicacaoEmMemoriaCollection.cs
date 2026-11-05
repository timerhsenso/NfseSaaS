using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Uma única aplicação em memória para todos os testes que fazem requisição
/// HTTP. Duas WebApplicationFactory subindo em paralelo disputam o logger
/// de bootstrap do Serilog (Program.cs) e uma delas falha com "The entry
/// point exited without ever building an IHost". Na mesma coleção, o xUnit
/// roda esses testes em sequência e reaproveita o host.
/// </summary>
[CollectionDefinition(Nome)]
public sealed class AplicacaoEmMemoriaCollection : ICollectionFixture<AplicacaoTesteFactory>
{
    public const string Nome = "Aplicação em memória";
}

/// <summary>
/// A aplicação dos testes de integração. Zera a chave e o token do Asaas
/// por cima dos user-secrets do PC do desenvolvedor: os testes nunca podem
/// falar com o Asaas de verdade (nem o sandbox). Sem isso, um job de verdade
/// do Hangfire, disparado pelo webhook num teste, chamava o sandbox e
/// disputava os mesmos registros com o teste.
/// </summary>
public sealed class AplicacaoTesteFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuracao) => configuracao.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Asaas:ApiKey"] = "",
            ["Asaas:WebhookToken"] = ""
        }));
    }
}
