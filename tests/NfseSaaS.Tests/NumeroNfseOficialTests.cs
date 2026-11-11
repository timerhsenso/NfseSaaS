using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Infrastructure.SincronizacaoSefin;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Número oficial da NFS-e: nunca o Id da nota ("NFS" + chave) nem o Id da DPS.</summary>
public class NumeroNfseOficialTests
{
    // cLocEmi(7) ambGer(1) tpInsc(1) CNPJ(14) nNFSe(13) AAMM(4) cNum(9) DV(1)
    private const string Chave = "2919207" + "2" + "2" + "04747304000178" + "0000000000044" + "2611" + "083642406" + "0";

    [Fact]
    public void Chave_de_teste_tem_50_digitos()
    {
        Assert.Equal(NumeroNfseOficial.TamanhoChave, Chave.Length);
    }

    [Fact]
    public void Numero_sai_da_chave_com_ou_sem_prefixo()
    {
        Assert.Equal("44", NumeroNfseOficial.DaChave(Chave));
        Assert.Equal("44", NumeroNfseOficial.DaChave("NFS" + Chave));
        Assert.Null(NumeroNfseOficial.DaChave("NFS123"));
        Assert.Null(NumeroNfseOficial.DaChave(null));
        Assert.Null(NumeroNfseOficial.DaChave("DPS" + new string('1', 47)));
    }

    [Theory]
    [InlineData("44", "44")]
    [InlineData("0000044", "44")]
    [InlineData(" 7 ", "7")]
    [InlineData("NFS" + Chave, "44")]
    [InlineData("DPS291920720474730400017800001000000000465396", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normaliza_para_o_numero_curto(string? valor, string? esperado)
    {
        Assert.Equal(esperado, NumeroNfseOficial.Normalizar(valor));
    }

    [Fact]
    public void So_corrige_o_que_nao_e_numero_limpo()
    {
        Assert.False(NumeroNfseOficial.PrecisaCorrigir("44"));
        Assert.False(NumeroNfseOficial.PrecisaCorrigir(null));
        Assert.True(NumeroNfseOficial.PrecisaCorrigir("NFS" + Chave));
        Assert.True(NumeroNfseOficial.PrecisaCorrigir("DPS123"));
        Assert.True(NumeroNfseOficial.PrecisaCorrigir("044"));
    }

    [Fact]
    public void Xml_usa_o_nNFSe_e_na_falta_o_numero_do_Id()
    {
        Assert.Equal("44", new NfseXmlImportDados(Xml("<nNFSe>44</nNFSe>")).NumeroNfse());
        Assert.Equal("44", new NfseXmlImportDados(Xml(string.Empty)).NumeroNfse());
    }

    private static string Xml(string numero) =>
        $"<NFSe xmlns=\"http://www.sped.fazenda.gov.br/nfse\" versao=\"1.01\"><infNFSe Id=\"NFS{Chave}\"><xLocEmi>Lauro de Freitas</xLocEmi>{numero}<cStat>100</cStat></infNFSe></NFSe>";
}
