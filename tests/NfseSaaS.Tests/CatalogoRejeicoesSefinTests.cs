using NfseSaaS.Application.Rejeicoes;
using Xunit;

namespace NfseSaaS.Tests;

public class CatalogoRejeicoesSefinTests
{
    [Fact]
    public void Catalogo_embutido_carrega_os_codigos_dos_anexos_oficiais()
    {
        Assert.True(CatalogoRejeicoesSefin.Quantidade >= 570, $"Só {CatalogoRejeicoesSefin.Quantidade} códigos no catálogo.");
        Assert.Contains("E0312", CatalogoRejeicoesSefin.Codigos);
        Assert.Contains("E1203", CatalogoRejeicoesSefin.Codigos);
    }

    [Fact]
    public void Todo_codigo_do_catalogo_tem_mensagem_area_e_orientacao()
    {
        foreach (var codigo in CatalogoRejeicoesSefin.Codigos)
        {
            var rejeicao = CatalogoRejeicoesSefin.Traduzir(codigo, null);

            Assert.NotNull(rejeicao);
            Assert.True(rejeicao!.NoCatalogo, codigo);
            Assert.False(string.IsNullOrWhiteSpace(rejeicao.MensagemOficial), $"{codigo} sem mensagem oficial");
            Assert.False(string.IsNullOrWhiteSpace(rejeicao.OQueFazer), $"{codigo} sem orientação");
            Assert.False(string.IsNullOrWhiteSpace(rejeicao.OndeCorrigir), $"{codigo} sem área");
        }
    }

    [Theory]
    [InlineData("E0312", AreaCorrecaoRejeicao.Servico, "/Servicos")]
    [InlineData("E0240", AreaCorrecaoRejeicao.Cliente, "/Clientes")]
    [InlineData("E0116", AreaCorrecaoRejeicao.Empresa, "/Empresas")]
    [InlineData("E0160", AreaCorrecaoRejeicao.Tributacao, "/Empresas")]
    [InlineData("E1203", AreaCorrecaoRejeicao.Certificado, "/Empresas")]
    [InlineData("E0015", AreaCorrecaoRejeicao.Nota, null)]
    public void Rejeicoes_comuns_apontam_a_tela_certa(string codigo, AreaCorrecaoRejeicao area, string? link)
    {
        var rejeicao = CatalogoRejeicoesSefin.Traduzir(codigo, "mensagem recebida")!;

        Assert.Equal(area, rejeicao.Area);
        Assert.Equal(link, rejeicao.LinkTela);
    }

    [Fact]
    public void Mensagem_recebida_da_SEFIN_tem_prioridade_sobre_a_do_catalogo()
    {
        var rejeicao = CatalogoRejeicoesSefin.Traduzir("e0312", "Texto que veio na resposta")!;

        Assert.Equal("E0312", rejeicao.Codigo);
        Assert.Equal("Texto que veio na resposta", rejeicao.MensagemOficial);
    }

    [Fact]
    public void Codigo_novo_fora_do_catalogo_continua_com_a_mensagem_recebida()
    {
        var rejeicao = CatalogoRejeicoesSefin.Traduzir("E9999", "Regra nova")!;

        Assert.False(rejeicao.NoCatalogo);
        Assert.Equal("Regra nova", rejeicao.MensagemOficial);
        Assert.Equal(AreaCorrecaoRejeicao.Suporte, rejeicao.Area);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("VALIDACAO-LOCAL")]
    [InlineData("CERTIFICADO")]
    public void Erros_locais_e_vazios_nao_sao_traduzidos(string? codigo)
    {
        Assert.Null(CatalogoRejeicoesSefin.Traduzir(codigo, "qualquer"));
    }

    [Theory]
    [InlineData("NFSe/infNFSe/DPS/infDPS/toma/end", "CEP", AreaCorrecaoRejeicao.Cliente)]
    [InlineData("NFSe/infNFSe/DPS/infDPS/valores/trib/tribMun/", "tpRetISSQN", AreaCorrecaoRejeicao.Cliente)]
    [InlineData("NFSe/infNFSe/DPS/infDPS/valores/trib/tribMun/", "pAliq", AreaCorrecaoRejeicao.Tributacao)]
    [InlineData("NFSe/infNFSe/DPS/infDPS/IBSCBS/valores", "cClassTrib", AreaCorrecaoRejeicao.ReformaTributaria)]
    [InlineData("evento/pedRegEvento/infPedReg/", "chNFSe", AreaCorrecaoRejeicao.Cancelamento)]
    [InlineData("NFSe/infNFSe/DPS/infDPS/", "serie", AreaCorrecaoRejeicao.Suporte)]
    [InlineData("NFSe/infNFSe/DPS/infDPS/prest/", "IM", AreaCorrecaoRejeicao.Empresa)]
    public void Classifica_pelo_caminho_do_campo(string caminho, string campo, AreaCorrecaoRejeicao esperada)
    {
        Assert.Equal(esperada, CatalogoRejeicoesSefin.ClassificarPorCampo(caminho, campo));
    }

    [Fact]
    public void Todas_traz_cada_codigo_uma_vez_em_ordem()
    {
        var codigos = CatalogoRejeicoesSefin.Todas.Select(r => r.Codigo).ToList();

        Assert.Equal(CatalogoRejeicoesSefin.Quantidade, codigos.Count);
        Assert.Equal(codigos.OrderBy(c => c, StringComparer.Ordinal), codigos);
        Assert.All(CatalogoRejeicoesSefin.Todas, r => Assert.True(r.NoCatalogo));
    }

    [Fact]
    public void Origem_oficial_identifica_o_anexo_de_cada_codigo()
    {
        Assert.All(CatalogoRejeicoesSefin.Codigos, codigo =>
            Assert.NotNull(CatalogoRejeicoesSefin.OrigemOficial(codigo).Documento));

        Assert.Equal((null, null), CatalogoRejeicoesSefin.OrigemOficial("E9999"));
    }
}
