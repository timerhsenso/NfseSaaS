using NfseSaaS.Application.UseCases.SincronizacaoSefin;
using NfseSaaS.Nacional.Clients;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Resposta do ADN na busca de notas: lote, fila vazia ou recusa com motivo.</summary>
public class RespostaAdnTests
{
    [Fact]
    public void Lote_com_documentos_e_lido()
    {
        const string corpo = """
            {"StatusProcessamento":"DOCUMENTOS_LOCALIZADOS","LoteDFe":[{"NSU":245,"ChaveAcesso":"CHAVE","TipoDocumento":"NFSE","ArquivoXml":"H4sI","DataHoraGeracao":"2026-11-07T10:00:00-03:00"}],"Alertas":[],"Erros":[],"TipoAmbiente":"HOMOLOGACAO","VersaoAplicativo":"1.0.0.0","DataHoraProcessamento":"2026-11-07T19:56:35.4237439-03:00"}
            """;
        var leitura = RespostaAdn.Interpretar(200, corpo);
        Assert.False(leitura.Recusada);
        var item = Assert.Single(leitura.Lote!.LoteDFe);
        Assert.Equal(245, item.NSU);
        Assert.Equal("NFSE", item.TipoDocumento);
    }

    [Fact]
    public void Fila_vazia_404_nao_e_erro()
    {
        var leitura = RespostaAdn.Interpretar(404, """{"StatusProcessamento":"NENHUM_DOCUMENTO_LOCALIZADO","LoteDFe":[],"Erros":[]}""");
        Assert.False(leitura.Recusada);
        Assert.Empty(leitura.Lote!.LoteDFe);
    }

    [Fact]
    public void Rejeicao_sem_erros_e_recusa_sem_motivo()
    {
        // Resposta real de 07/11/2026 (homologação).
        const string corpo = """
            {"TipoConsulta":"InscricaoCertificado","StatusProcessamento":"REJEICAO","LoteDFe":[],"Alertas":[],"Erros":[],"TipoAmbiente":"HOMOLOGACAO","VersaoAplicativo":"1.0.0.0","DataHoraProcessamento":"2026-11-07T19:56:35.4237439-03:00"}
            """;
        var leitura = RespostaAdn.Interpretar(400, corpo);
        Assert.True(leitura.Recusada);
        Assert.Null(leitura.Motivo);
        Assert.Contains("sem informar o motivo", MensagensSincronizacao.Recusa(leitura.Motivo));
    }

    [Fact]
    public void Erros_em_objeto_ou_texto_viram_o_motivo()
    {
        var objeto = RespostaAdn.Interpretar(400, """{"StatusProcessamento":"REJEICAO","Erros":[{"Codigo":"E2243","Descricao":"CNPJ de outra raiz","Complemento":null}]}""");
        Assert.Equal("E2243 - CNPJ de outra raiz", objeto.Motivo);
        Assert.Equal("A SEFIN Nacional recusou a busca de notas desta empresa: E2243 - CNPJ de outra raiz." + MensagensSincronizacao.NadaAlterado,
            MensagensSincronizacao.Recusa(objeto.Motivo));

        var texto = RespostaAdn.Interpretar(400, """{"Erros":["Certificado inválido."]}""");
        Assert.Equal("Certificado inválido.", texto.Motivo);
    }

    [Fact]
    public void Corpo_que_nao_e_json_e_recusa_sem_motivo()
    {
        var leitura = RespostaAdn.Interpretar(503, "<html>Service Unavailable</html>");
        Assert.True(leitura.Recusada);
        Assert.Null(leitura.Motivo);

        Assert.True(RespostaAdn.Interpretar(404, "").Recusada);
    }
}
