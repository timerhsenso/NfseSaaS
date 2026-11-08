using NfseSaaS.Infrastructure.Danfse;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// F1 parte 2 — bloco "TRIBUTAÇÃO IBS / CBS" do DANFSe preenchido com o que
/// a SEFIN calculou. Os valores do grupo IBSCBS abaixo são os da nota real
/// autorizada em homologação em 08/11/2026 (Simples Nacional, cTribNac
/// 340101, tomador em Salvador); o resto do XML é mínimo e sem dados reais.
/// </summary>
public class DanfseIbsCbsTests
{
    private const string XmlComIbsCbs = """
        <?xml version="1.0" encoding="utf-8"?>
        <NFSe versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse">
          <infNFSe Id="NFS29192072200000000000100000000000003626116287837861">
            <xLocEmi>Lauro de Freitas</xLocEmi>
            <nNFSe>36</nNFSe>
            <cStat>100</cStat>
            <valores><vLiq>1.00</vLiq></valores>
            <IBSCBS>
              <cLocalidadeIncid>2927408</cLocalidadeIncid>
              <xLocalidadeIncid>Salvador</xLocalidadeIncid>
              <valores>
                <vBC>1.00</vBC>
                <uf><pIBSUF>0.10</pIBSUF><pAliqEfetUF>0.10</pAliqEfetUF></uf>
                <mun><pIBSMun>0.00</pIBSMun><pAliqEfetMun>0.00</pAliqEfetMun></mun>
                <fed><pCBS>0.90</pCBS><pAliqEfetCBS>0.90</pAliqEfetCBS></fed>
              </valores>
              <totCIBS>
                <vTotNF>1.00</vTotNF>
                <gIBS>
                  <vIBSTot>0.00</vIBSTot>
                  <gIBSUFTot><vIBSUF>0.00</vIBSUF></gIBSUFTot>
                  <gIBSMunTot><vIBSMun>0.00</vIBSMun></gIBSMunTot>
                </gIBS>
                <gCBS><vCBS>0.01</vCBS></gCBS>
              </totCIBS>
            </IBSCBS>
            <DPS versao="1.01">
              <infDPS>
                <tpAmb>2</tpAmb>
                <finNFSe>0</finNFSe>
                <valores><vServPrest><vServ>1.00</vServ></vServPrest></valores>
                <IBSCBS>
                  <cIndOp>100301</cIndOp>
                  <indDest>0</indDest>
                  <valores><trib><CST>000</CST><cClassTrib>000001</cClassTrib><gIBSCBS /></trib></valores>
                </IBSCBS>
              </infDPS>
            </DPS>
          </infNFSe>
        </NFSe>
        """;

    private const string XmlSemIbsCbs = """
        <?xml version="1.0" encoding="utf-8"?>
        <NFSe versao="1.00" xmlns="http://www.sped.fazenda.gov.br/nfse">
          <infNFSe Id="NFS29192072200000000000100000000000003626116287837861">
            <valores><vLiq>1.00</vLiq></valores>
            <DPS versao="1.00"><infDPS><tpAmb>2</tpAmb></infDPS></DPS>
          </infNFSe>
        </NFSe>
        """;

    [Fact]
    public void Bloco_mostra_a_classificacao_e_o_local_de_incidencia()
    {
        var d = new DanfseXmlDados(XmlComIbsCbs);

        Assert.True(d.TemIbsCbs());
        Assert.Equal("000 / 000001", d.IbsCbsCstEClasseTrib());
        Assert.Equal("100301 / 2927408 / Salvador / BA", d.IbsCbsIndicadorOperacao());
    }

    [Fact]
    public void Bloco_mostra_base_aliquotas_e_valores_calculados_pela_SEFIN()
    {
        var d = new DanfseXmlDados(XmlComIbsCbs);

        Assert.Equal("R$ 0,00", d.IbsCbsExclusoesReducoesBc());
        Assert.Equal("R$ 1,00", d.IbsCbsBaseCalculo());
        Assert.Equal("-", d.IbsCbsReducaoAliquota());
        Assert.Equal("0,10% / 0,00%", d.IbsCbsAliquotaUfMun());

        // Zero calculado pela SEFIN aparece como zero, não como "ausente".
        Assert.Equal("0,00%", d.IbsCbsAliquotaEfetivaMunicipal());
        Assert.Equal("R$ 0,00", d.IbsCbsValorApuradoMunicipal());
        Assert.Equal("0,10%", d.IbsCbsAliquotaEfetivaEstadual());
        Assert.Equal("R$ 0,00", d.IbsCbsValorApuradoEstadual());
        Assert.Equal("R$ 0,00", d.IbsCbsValorTotalApuradoIbs());

        Assert.Equal("0,90%", d.IbsCbsAliquota());
        Assert.Equal("0,90%", d.IbsCbsAliquotaEfetiva());
        Assert.Equal("R$ 0,01", d.IbsCbsValorTotalApuradoCbs());

        Assert.Equal("0,01", d.TotalIbsCbs());
        Assert.Equal("1,00", d.ValorLiquidoMaisIbsCbs());
    }

    [Fact]
    public void Reducoes_e_ajustes_da_base_entram_quando_a_SEFIN_devolve()
    {
        var xml = XmlComIbsCbs
            .Replace("<vBC>1.00</vBC>", "<vBC>1.00</vBC><vCalcAjusteBCLocImoveis>0.25</vCalcAjusteBCLocImoveis><vCalcAjusteBCIBSCBS>0.50</vCalcAjusteBCIBSCBS>")
            .Replace("<pIBSUF>0.10</pIBSUF>", "<pIBSUF>0.10</pIBSUF><pRedAliqUF>60.00</pRedAliqUF>")
            .Replace("<pCBS>0.90</pCBS>", "<pCBS>0.90</pCBS><pRedAliqCBS>60.00</pRedAliqCBS>");
        var d = new DanfseXmlDados(xml);

        Assert.Equal("R$ 0,75", d.IbsCbsExclusoesReducoesBc());
        Assert.Equal("60,00% / 60,00%", d.IbsCbsReducaoAliquota());
    }

    [Fact]
    public void Sem_o_grupo_o_bloco_sai_como_antes()
    {
        var d = new DanfseXmlDados(XmlSemIbsCbs);

        Assert.False(d.TemIbsCbs());
        Assert.Equal("-", d.IbsCbsCstEClasseTrib());
        Assert.Equal("-", d.IbsCbsIndicadorOperacao());
        Assert.Equal("R$ 0,00", d.IbsCbsExclusoesReducoesBc());
        Assert.Equal("-", d.IbsCbsBaseCalculo());
        Assert.Equal("-", d.IbsCbsReducaoAliquota());
        Assert.Equal("-", d.IbsCbsAliquotaUfMun());
        Assert.Equal("-", d.IbsCbsAliquotaEfetivaMunicipal());
        Assert.Equal("-", d.IbsCbsValorApuradoMunicipal());
        Assert.Equal("-", d.IbsCbsValorTotalApuradoIbs());
        Assert.Equal("-", d.IbsCbsAliquota());
        Assert.Equal("-", d.IbsCbsValorTotalApuradoCbs());
        Assert.Equal("0,00", d.TotalIbsCbs());
        Assert.Equal("0,00", d.ValorLiquidoMaisIbsCbs());
    }

    [Fact]
    public void Cst_sem_tributacao_mostra_so_a_classificacao()
    {
        var xml = XmlComIbsCbs
            .Replace("<cIndOp>100301</cIndOp>", "")
            .Replace("<CST>000</CST><cClassTrib>000001</cClassTrib><gIBSCBS />", "<CST>400</CST><cClassTrib>400001</cClassTrib>");
        var d = new DanfseXmlDados(xml);

        Assert.Equal("400 / 400001", d.IbsCbsCstEClasseTrib());
        Assert.Equal("- / 2927408 / Salvador / BA", d.IbsCbsIndicadorOperacao());
    }

    [Fact]
    public void Pdf_e_gerado_com_o_grupo_IBSCBS()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var pdf = DanfsePdfGenerator.Gerar(XmlComIbsCbs, cancelada: false);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
