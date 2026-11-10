using System.Xml.Linq;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>C2: local da prestação por nota (nota → contrato → empresa) e cLocPrestacao na DPS.</summary>
public class LocalPrestacaoTests
{
    private const string Empresa = "2919207";   // Lauro de Freitas
    private const string Contrato = "2927408";  // Salvador
    private const string Nota = "3550308";      // São Paulo

    [Fact]
    public void Ordem_nota_contrato_empresa()
    {
        Assert.Equal(Nota, RegrasLocalPrestacao.Resolver(Nota, Contrato, Empresa));
        Assert.Equal(Contrato, RegrasLocalPrestacao.Resolver(null, Contrato, Empresa));
        Assert.Equal(Empresa, RegrasLocalPrestacao.Resolver(null, null, Empresa));
    }

    [Fact]
    public void Em_branco_na_nota_e_o_da_empresa_mesmo_com_contrato()
    {
        Assert.Equal(Empresa, RegrasLocalPrestacao.Resolver("", Contrato, Empresa));
        Assert.Equal(Empresa, RegrasLocalPrestacao.Resolver("  ", Contrato, Empresa));
    }

    [Fact]
    public void Normaliza_e_nao_guarda_o_da_propria_empresa()
    {
        Assert.Equal("2927408", RegrasLocalPrestacao.Normalizar(" 2927-408 "));
        Assert.Null(RegrasLocalPrestacao.Normalizar(""));
        Assert.Null(RegrasLocalPrestacao.ParaGuardar(Empresa, Empresa));
        Assert.Null(RegrasLocalPrestacao.ParaGuardar(null, Empresa));
        Assert.Equal(Contrato, RegrasLocalPrestacao.ParaGuardar(Contrato, Empresa));
    }

    private static string CLocPrestacao(string xml) =>
        XDocument.Parse(xml).Descendants().Single(e => e.Name.LocalName == "cLocPrestacao").Value;

    [Fact]
    public void Dps_leva_o_local_escolhido_ou_o_do_prestador()
    {
        var builder = DpsBuilderFactory.Criar();
        var request = DpsRequestFactory.CriarRequestDeTeste();

        Assert.Equal(request.Prestador.CodigoMunicipio, CLocPrestacao(builder.Construir(request).XmlDps));
        Assert.Equal(Nota, CLocPrestacao(builder.Construir(request with { CodigoMunicipioPrestacao = Nota }).XmlDps));
        Assert.Equal(request.Prestador.CodigoMunicipio, CLocPrestacao(builder.Construir(request with { CodigoMunicipioPrestacao = " " }).XmlDps));
    }
}
