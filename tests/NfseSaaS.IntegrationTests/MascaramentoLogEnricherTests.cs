using NfseSaaS.Web.Monitoramento;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>SEG1 — o enricher do Serilog mascara antes de qualquer saída (mensagem renderizada inclusive).</summary>
public class MascaramentoLogEnricherTests
{
    private sealed class Coletor : ILogEventSink
    {
        public List<LogEvent> Eventos { get; } = [];
        public void Emit(LogEvent logEvent) => Eventos.Add(logEvent);
    }

    [Fact]
    public void Senha_token_e_cpf_somem_do_evento_e_da_mensagem()
    {
        var coletor = new Coletor();
        using var logger = new LoggerConfiguration()
            .Enrich.With<MascaramentoLogEnricher>()
            .WriteTo.Sink(coletor)
            .CreateLogger();

        logger.Error("Login de {Email} com {Senha}; Asaas respondeu {Corpo}; pedido {@Pedido}; tokens {Lista}",
            "a@b.com", "minha-senha",
            """{"cpfCnpj":"12345678909","access_token":"xyz"}""",
            new { EmpresaId = 7, SenhaCertificado = "pfx-123", Link = "https://x/copia/confirmar?c=abc123" },
            new[] { "token=t1", "ok" });

        var evento = Assert.Single(coletor.Eventos);
        var mensagem = evento.RenderMessage();
        foreach (var segredo in new[] { "minha-senha", "12345678909", "xyz", "pfx-123", "abc123", "t1" })
            Assert.DoesNotContain(segredo, mensagem);
        Assert.Contains("a@b.com", mensagem);
        Assert.Contains("EmpresaId: 7", mensagem);
        Assert.Equal("\"***\"", evento.Properties["Senha"].ToString());
    }
}
