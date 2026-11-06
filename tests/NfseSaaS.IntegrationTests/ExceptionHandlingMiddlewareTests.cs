using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Web.Middleware;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Em PRODUÇÃO, regra de negócio e "não encontrado" mostram a mensagem
/// (é texto para o usuário); erro inesperado continua genérico.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private sealed class AmbienteProducao : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "NfseSaaS.Web";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<(int Status, string Erro)> ExecutarAsync(Exception ex)
    {
        var middleware = new ExceptionHandlingMiddleware(_ => throw ex,
            NullLogger<ExceptionHandlingMiddleware>.Instance, new AmbienteProducao());
        var contexto = new DefaultHttpContext();
        contexto.Request.Path = "/api/assinatura/trocar-plano";
        contexto.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(contexto);

        contexto.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(contexto.Response.Body);
        return (contexto.Response.StatusCode, json.RootElement.GetProperty("erro").GetString()!);
    }

    [Fact]
    public async Task Regra_de_negocio_mostra_a_mensagem_em_producao()
    {
        var (status, erro) = await ExecutarAsync(new RegraNegocioException("Pagamento: O valor mínimo é R$ 5,00."));
        Assert.Equal(422, status);
        Assert.Equal("Pagamento: O valor mínimo é R$ 5,00.", erro);
    }

    [Fact]
    public async Task Erro_inesperado_continua_generico_em_producao()
    {
        var (status, erro) = await ExecutarAsync(new InvalidOperationException("detalhe interno"));
        Assert.Equal(500, status);
        Assert.Equal("Ocorreu um erro ao processar a solicitação.", erro);
    }
}
