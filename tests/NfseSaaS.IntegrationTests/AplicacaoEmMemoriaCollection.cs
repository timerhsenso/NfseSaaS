using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Consultas;
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
/// disputava os mesmos registros com o teste. Pelo mesmo motivo a consulta
/// de CNPJ (BrasilAPI) é trocada por <see cref="ConsultaCnpjFalsa"/>.
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
        builder.ConfigureTestServices(servicos => servicos.AddScoped<IConsultarCnpjUseCase, ConsultaCnpjFalsa>());
    }
}

/// <summary>
/// "Receita" dos testes: todo CNPJ existe, ativo, optante do Simples, em
/// Salvador/BA. Começando com "99": BAIXADA. Começando com "98": serviço
/// fora do ar. Começando com "97": não encontrado.
/// </summary>
public sealed class ConsultaCnpjFalsa : IConsultarCnpjUseCase
{
    public Task<ConsultaCnpjResponse> ExecutarAsync(string cnpj, CancellationToken cancellationToken)
    {
        var limpo = new string(cnpj.Where(char.IsLetterOrDigit).ToArray());
        if (limpo.StartsWith("98"))
            throw new RegraNegocioException("Não foi possível consultar o CNPJ agora (serviço externo indisponível).");
        if (limpo.StartsWith("97"))
            throw new RecursoNaoEncontradoException($"CNPJ {limpo} não encontrado na Receita Federal.");

        return Task.FromResult(new ConsultaCnpjResponse(
            limpo, $"EMPRESA TESTE {limpo} LTDA", "Fantasia Teste", "Rua do Teste", "100", null, "Centro", "40000000",
            "2927408", "BA", "7133445566", "contato@receita.teste",
            limpo.StartsWith("99") ? "BAIXADA" : "ATIVA",
            Municipio: "SALVADOR", OptanteSimples: true, OptanteMei: false));
    }
}
