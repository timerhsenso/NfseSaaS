using Microsoft.AspNetCore.Mvc.Testing;
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
public sealed class AplicacaoEmMemoriaCollection : ICollectionFixture<WebApplicationFactory<Program>>
{
    public const string Nome = "Aplicação em memória";
}
