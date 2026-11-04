using Xunit;
using System.Net;
using Microsoft.Extensions.Options;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.IndicesEconomicos;

namespace NfseSaaS.Tests;

public sealed class BancoCentralSgsClientTests
{
    private sealed class HandlerFalso : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _corpo;
        public Uri? UltimaUrl { get; private set; }

        public HandlerFalso(HttpStatusCode status, string corpo)
        {
            _status = status;
            _corpo = corpo;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaUrl = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_corpo) });
        }
    }

    private static (BancoCentralSgsClient Cliente, HandlerFalso Handler) Criar(HttpStatusCode status, string corpo)
    {
        var handler = new HandlerFalso(status, corpo);
        return (new BancoCentralSgsClient(new HttpClient(handler), Options.Create(new IndicesEconomicosOptions())), handler);
    }

    [Fact]
    public void Interpretar_LeDataBrasileiraEValorComPonto()
    {
        var r = BancoCentralSgsClient.Interpretar(
            """[{"data":"01/09/2026","valor":"0.48"},{"data":"01/10/2026","valor":"-0.11"}]""", 433);

        Assert.Equal(2, r.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), r[0].Competencia);
        Assert.Equal(0.48m, r[0].Variacao);
        Assert.Equal(-0.11m, r[1].Variacao);
    }

    [Fact]
    public void Interpretar_IgnoraValorVazio()
    {
        var r = BancoCentralSgsClient.Interpretar("""[{"data":"01/09/2026","valor":""}]""", 433);
        Assert.Empty(r);
    }

    [Fact]
    public void Interpretar_HtmlNoLugarDeJson_DaErroClaro()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BancoCentralSgsClient.Interpretar("<html>manutenção</html>", 433));
        Assert.Contains("SGS 433", ex.Message);
    }

    [Fact]
    public async Task ObterVariacoes_MontaUrlComPeriodoDoMesInteiro()
    {
        var (cliente, handler) = Criar(HttpStatusCode.OK, "[]");

        await cliente.ObterVariacoesAsync(IndiceReajusteContrato.Igpm, new DateOnly(2024, 11, 15), new DateOnly(2026, 10, 1), CancellationToken.None);

        Assert.Equal(
            "https://api.bcb.gov.br/dados/serie/bcdata.sgs.189/dados?formato=json&dataInicial=01/11/2024&dataFinal=31/10/2026",
            handler.UltimaUrl!.OriginalString);
    }

    [Fact]
    public async Task ObterVariacoes_404SemDadosNoPeriodo_ListaVazia()
    {
        var (cliente, _) = Criar(HttpStatusCode.NotFound, """{"error":"Value(s) not found"}""");

        var r = await cliente.ObterVariacoesAsync(IndiceReajusteContrato.Ipca, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), CancellationToken.None);

        Assert.Empty(r);
    }

    [Fact]
    public async Task ObterVariacoes_ErroDoServidor_Lanca()
    {
        var (cliente, _) = Criar(HttpStatusCode.ServiceUnavailable, "fora do ar");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cliente.ObterVariacoesAsync(IndiceReajusteContrato.Ipca, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1), CancellationToken.None));
    }

    [Fact]
    public void IndicesSuportados_NaoIncluiOutro()
    {
        var (cliente, _) = Criar(HttpStatusCode.OK, "[]");

        Assert.DoesNotContain(IndiceReajusteContrato.Outro, cliente.IndicesSuportados);
        Assert.Equal(6, cliente.IndicesSuportados.Count);
        Assert.Equal("SGS 433", cliente.DescreverSerie(IndiceReajusteContrato.Ipca));
    }
}
