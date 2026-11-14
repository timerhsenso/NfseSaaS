using NfseSaaS.Application.Legal;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Legal;
using Xunit;

namespace NfseSaaS.Tests;

public class DocumentosLegaisTests
{
    [Fact]
    public void Versoes_cabem_na_coluna_do_aceite_e_estao_em_ordem_com_aviso_previo()
    {
        Assert.Equal("1.0", DocumentosLegais.Versoes[0].Versao);
        for (var i = 0; i < DocumentosLegais.Versoes.Count; i++)
        {
            var v = DocumentosLegais.Versoes[i];
            // aceites_termos.VersaoTermosDeUso / aceites_instrumentos.Versao: varchar(20)
            Assert.InRange(v.Versao.Length, 1, 20);
            Assert.False(string.IsNullOrWhiteSpace(v.ResumoMudancas));
            Assert.Equal(i, DocumentosLegais.Ordem(v.Versao));
            if (i == 0)
                continue;

            var anterior = DocumentosLegais.Versoes[i - 1];
            Assert.True(v.PublicadaEm >= anterior.PublicadaEm);
            Assert.True(v.VigenteDesde > anterior.VigenteDesde);
            // Termos, item 17: pelo menos 15 dias entre publicar e valer.
            Assert.True(v.VigenteDesde.DayNumber - v.PublicadaEm.DayNumber >= 15, $"Versão {v.Versao} sem 15 dias de aviso prévio.");
        }
        Assert.False(DocumentosLegais.PossuiMarcadorPendente);
    }

    [Theory]
    [InlineData("2026-11-14", "1.1", "1.0")] // 1.1 publicada, ainda não vale
    [InlineData("2026-11-24", "1.2", "1.1")] // 1.1 passa a valer no dia em que a 1.2 é publicada
    [InlineData("2026-12-08", "1.2", "1.1")]
    [InlineData("2026-12-09", "1.2", "1.2")]
    public void Atual_e_vigente_pela_data(string data, string atual, string vigente)
    {
        var hoje = DateOnly.Parse(data);
        Assert.Equal(atual, DocumentosLegais.Atual(hoje).Versao);
        Assert.Equal(vigente, DocumentosLegais.Vigente(hoje).Versao);
        Assert.Equal(atual != vigente, DocumentosLegais.EmAvisoPrevio(hoje));
    }

    [Fact]
    public void Aceite_de_versao_mais_nova_vale_para_as_anteriores()
    {
        var dia24 = new DateOnly(2026, 11, 24);
        Assert.False(DocumentosLegais.AceiteLibera("1.0", dia24));
        Assert.True(DocumentosLegais.AceiteLibera("1.1", dia24));
        Assert.True(DocumentosLegais.AceiteLibera("1.2", dia24)); // aceite antecipado

        var dia9 = new DateOnly(2026, 12, 9);
        Assert.False(DocumentosLegais.AceiteLibera("1.1", dia9));
        Assert.True(DocumentosLegais.AceiteLibera("1.2", dia9));
        Assert.False(DocumentosLegais.AceiteLibera("9.9", dia9));
        Assert.False(DocumentosLegais.AceiteLibera(null, dia9));
    }

    [Fact]
    public void Versao_nao_publicada_nao_aparece()
    {
        Assert.Null(DocumentosLegais.Publicada("1.2", new DateOnly(2026, 11, 23)));
        Assert.NotNull(DocumentosLegais.Publicada("1.2", new DateOnly(2026, 11, 24)));
        Assert.Null(DocumentosLegais.Publicada("7.0", new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Todo_instrumento_citado_tem_texto_embutido_e_hash_estavel()
    {
        foreach (var versao in DocumentosLegais.Versoes)
        {
            foreach (var tipo in new[] { TipoDocumentoLegal.Dpa, TipoDocumentoLegal.AutorizacaoCertificado, TipoDocumentoLegal.AutorizacaoEmissaoAutomatica })
            {
                if (DocumentosLegais.VersaoInstrumento(tipo, versao) is not { } v)
                    continue;
                var texto = TextosInstrumentos.ObterObrigatorio(tipo, v);
                Assert.Matches("^[0-9a-f]{64}$", texto.Hash);
                Assert.Contains("<h2>", texto.Html);
                // Mesmo texto com quebra de linha do Windows (git autocrlf) dá o mesmo hash.
                Assert.Equal(texto.Hash, TextosInstrumentos.CalcularHash(TextosInstrumentos.Normalizar(texto.Html.Replace("\n", "\r\n"))));
            }
        }
        Assert.Null(TextosInstrumentos.Obter(TipoDocumentoLegal.Dpa, "9.9"));
    }

    [Fact]
    public void Email_de_privacidade_e_de_suporte_sao_do_dominio_do_servico()
    {
        Assert.EndsWith("@" + DocumentosLegais.Site, DocumentosLegais.EmailPrivacidade);
        Assert.EndsWith("@" + DocumentosLegais.Site, DocumentosLegais.EmailSuporte);
    }

    [Fact]
    public void Origens_do_aceite_cabem_na_coluna()
    {
        // aceites_termos.Origem e aceites_instrumentos.Origem: varchar(30)
        foreach (var origem in new[]
                 {
                     OrigemAceiteTermos.Cadastro, OrigemAceiteTermos.Convite, OrigemAceiteTermos.Atualizacao,
                     OrigemAceiteInstrumento.Cadastro, OrigemAceiteInstrumento.Atualizacao, OrigemAceiteInstrumento.Certificado, OrigemAceiteInstrumento.Automacao
                 })
            Assert.InRange(origem.Length, 1, 30);
    }

    [Fact]
    public void Aceite_novo_ja_nasce_com_data()
    {
        var aceite = new AceiteTermos();

        Assert.NotEqual(Guid.Empty, aceite.Id);
        Assert.True(aceite.AceitoEm > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Comprovante_tira_o_texto_do_html_e_gera_pdf()
    {
        var texto = TextosInstrumentos.ObterObrigatorio(TipoDocumentoLegal.Dpa, "1.0");
        var paragrafos = AceiteInstrumentosService.ParagrafosDoHtml(texto.Html);
        Assert.Contains(paragrafos, p => p.Titulo && p.Texto.StartsWith("1. Objeto"));
        Assert.Contains(paragrafos, p => p.Texto.StartsWith("Hostinger | "));
        Assert.DoesNotContain(paragrafos, p => p.Texto.Contains('<'));

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = AceiteInstrumentosService.GerarComprovante(
            [new DocumentoAceitoResponse(Guid.NewGuid(), "instrumento", TipoDocumentoLegal.Dpa, DocumentosLegais.Nome(TipoDocumentoLegal.Dpa), "1.0", "/dpa/1.0",
                "admin@teste.com", null, "12345678000195", DateTimeOffset.UtcNow, "127.0.0.1", texto.Hash, null, null, null)],
            "Conta Teste", "12345678000195", texto.Html);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
