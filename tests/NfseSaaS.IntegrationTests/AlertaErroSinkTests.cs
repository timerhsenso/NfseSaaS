using NfseSaaS.Infrastructure.Monitoramento;
using NfseSaaS.Web.Monitoramento;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>O que o sink manda (ou não) para a fila de alertas do operador.</summary>
public class AlertaErroSinkTests
{
    [Fact]
    public async Task Error_vira_alerta_com_mensagem_origem_e_excecao()
    {
        var fila = new AlertaOperadorFila();
        new AlertaErroSink(fila).Emit(Evento(LogEventLevel.Error, "NfseSaaS.Infrastructure.Jobs.GerarReajustesAniversarioJob", new InvalidOperationException("falhou")));

        var alerta = await LerUmAsync(fila);
        Assert.NotNull(alerta);
        Assert.Equal("Erro", alerta!.Nivel);
        Assert.Equal("NfseSaaS.Infrastructure.Jobs.GerarReajustesAniversarioJob", alerta.Origem);
        Assert.Contains("falhou", alerta.Excecao);
        Assert.Equal("abc-123", alerta.CorrelationId);
    }

    [Theory]
    [InlineData(LogEventLevel.Warning, "NfseSaaS.Web.Middleware.ExceptionHandlingMiddleware")]
    [InlineData(LogEventLevel.Error, "NfseSaaS.Infrastructure.Email.SmtpEmailSender")]
    [InlineData(LogEventLevel.Error, "NfseSaaS.Infrastructure.Monitoramento.AlertaOperadorHostedService")]
    [InlineData(LogEventLevel.Error, "Hangfire.Processing.BackgroundExecution")]
    public async Task Warning_envio_de_email_monitoramento_e_hangfire_nao_viram_alerta(LogEventLevel nivel, string origem)
    {
        var fila = new AlertaOperadorFila();
        new AlertaErroSink(fila).Emit(Evento(nivel, origem, null));

        Assert.Null(await LerUmAsync(fila));
    }

    [Fact]
    public async Task Falha_definitiva_de_job_registrada_pelo_filtro_vira_alerta()
    {
        var fila = new AlertaOperadorFila();
        new AlertaErroSink(fila).Emit(Evento(LogEventLevel.Error, "NfseSaaS.Infrastructure.Monitoramento.AlertaFalhaJobFilter", new TimeoutException()));

        Assert.NotNull(await LerUmAsync(fila));
    }

    private static LogEvent Evento(LogEventLevel nivel, string origem, Exception? excecao) =>
        new(DateTimeOffset.UtcNow, nivel, excecao,
            new MessageTemplateParser().Parse("Algo deu errado na Empresa {EmpresaId}"),
            new[]
            {
                new LogEventProperty("SourceContext", new ScalarValue(origem)),
                new LogEventProperty("CorrelationId", new ScalarValue("abc-123")),
                new LogEventProperty("EmpresaId", new ScalarValue(Guid.Empty))
            });

    private static async Task<AlertaOperador?> LerUmAsync(AlertaOperadorFila fila)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await foreach (var alerta in fila.LerTodosAsync(cts.Token))
                return alerta;
        }
        catch (OperationCanceledException)
        {
        }
        return null;
    }
}
