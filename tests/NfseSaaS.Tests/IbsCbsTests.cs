using System.Xml.Linq;
using NfseSaaS.Application.Fiscal.IbsCbs;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// F1 — IBS/CBS na DPS: tabelas oficiais embutidas (Anexos VII e VIII),
/// regras de quando e como informar (NT 009, Anexo VI v1.04.01) e o grupo
/// IBSCBS validado contra o XSD oficial de Produção Restrita.
/// </summary>
public class IbsCbsTests
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    // ---------- Catálogo ----------

    [Fact]
    public void Catalogo_carrega_os_anexos_VII_e_VIII()
    {
        Assert.Equal("1.03.00", CatalogoIbsCbs.VersaoAnexoVII);
        Assert.Equal("1.02.00", CatalogoIbsCbs.VersaoAnexoVIII);
        Assert.Equal(40, CatalogoIbsCbs.IndicadoresOperacao.Count);
        Assert.Equal(32, CatalogoIbsCbs.Classificacoes.Count);

        var licenca = CatalogoIbsCbs.OpcoesDoServico("010501")!;
        Assert.Equal("0105", licenca.Item);
        Assert.Contains("111032100", licenca.Nbs);
        Assert.Equal("100501", Assert.Single(licenca.IndicadoresOperacao).Codigo);
        Assert.Equal("000001", Assert.Single(licenca.Classificacoes).Codigo);

        Assert.Null(CatalogoIbsCbs.OpcoesDoServico("999999"));
        Assert.Null(CatalogoIbsCbs.OpcoesDoServico(null));
    }

    [Fact]
    public void Indicador_so_e_emitivel_sem_imovel_evento_ou_destinatario()
    {
        Assert.True(CatalogoIbsCbs.IndicadorOperacao("100301")!.SuportadoPeloSistema);   // endereço do tomador
        Assert.True(CatalogoIbsCbs.IndicadorOperacao("030101")!.SuportadoPeloSistema);   // estabelecimento do prestador
        Assert.True(CatalogoIbsCbs.IndicadorOperacao("030102")!.SuportadoPeloSistema);   // local da prestação
        Assert.False(CatalogoIbsCbs.IndicadorOperacao("020201")!.SuportadoPeloSistema);  // imóvel (gUnidImob, E0938)
        Assert.False(CatalogoIbsCbs.IndicadorOperacao("040101")!.SuportadoPeloSistema);  // evento
        Assert.False(CatalogoIbsCbs.IndicadorOperacao("100302")!.SuportadoPeloSistema);  // endereço do destinatário
        Assert.False(CatalogoIbsCbs.IndicadorOperacao("010104")!.SuportadoPeloSistema);  // não vale para NFS-e
    }

    // ---------- Quando informar ----------

    [Theory]
    // Não optante: a partir de 01/01/2026 (E0850), em qualquer ambiente.
    [InlineData("1", "2025-12-01", true, false)]
    [InlineData("1", "2026-01-01", true, true)]
    [InlineData("1", "2026-11-01", false, true)]
    // Simples (ME/EPP e MEI): produção só a partir de 2027; homologação já agora.
    [InlineData("3", "2026-12-01", true, false)]
    [InlineData("3", "2026-12-01", false, true)]
    [InlineData("3", "2027-01-01", true, true)]
    [InlineData("2", "2026-11-01", true, false)]
    [InlineData("2", "2027-02-01", true, true)]
    public void Grupo_vai_conforme_regime_competencia_e_ambiente(string opSimpNac, string competencia, bool producao, bool esperado)
    {
        Assert.Equal(esperado, RegrasIbsCbs.DeveInformar(
            opSimpNac, DateOnly.Parse(competencia), producao, RegrasIbsCbs.InicioSimplesNacionalPadrao, simplesNacionalEmHomologacao: true));
    }

    [Fact]
    public void Simples_em_homologacao_pode_ser_desligado_pela_configuracao()
    {
        Assert.False(RegrasIbsCbs.DeveInformar("3", new DateOnly(2026, 12, 1), producao: false, RegrasIbsCbs.InicioSimplesNacionalPadrao, simplesNacionalEmHomologacao: false));
    }

    // ---------- Como informar ----------

    [Fact]
    public void Sem_nada_no_servico_vale_a_sugestao_da_tabela_oficial()
    {
        var licenca = RegrasIbsCbs.Resolver("010501", null, null).Classificacao!;
        Assert.Equal(("100501", "000", "000001", true, true), (licenca.CIndOp, licenca.Cst, licenca.CClassTrib, licenca.ComTributacao, licenca.Sugerida));

        var contabilidade = RegrasIbsCbs.Resolver("171901", null, null).Classificacao!;
        Assert.Equal(("100301", "200", "200052"), (contabilidade.CIndOp, contabilidade.Cst, contabilidade.CClassTrib));

        // 04.01: o primeiro indicador emitível da lista.
        Assert.Equal("030101", RegrasIbsCbs.Resolver("040101", null, null).Classificacao!.CIndOp);
    }

    [Fact]
    public void O_que_o_servico_fixar_vale_mais_que_a_sugestao()
    {
        var r = RegrasIbsCbs.Resolver("040101", "100301", "200029").Classificacao!;
        Assert.Equal(("100301", "200029", false), (r.CIndOp, r.CClassTrib, r.Sugerida));
    }

    [Fact]
    public void Cst_sem_tributacao_nao_leva_indicador_nem_grupo_de_tributacao()
    {
        // 16.01 (transporte coletivo): primeira classificação 400001 (isenção).
        var r = RegrasIbsCbs.Resolver("160101", null, null).Classificacao!;
        Assert.Equal(("400", "400001", false), (r.Cst, r.CClassTrib, r.ComTributacao));
        Assert.Null(r.CIndOp);
        Assert.False(RegrasIbsCbs.TemTributacao("410"));
        Assert.False(RegrasIbsCbs.TemTributacao("820"));
        Assert.True(RegrasIbsCbs.TemTributacao("000"));
    }

    [Fact]
    public void Servico_que_so_admite_indicador_de_imovel_para_com_explicacao()
    {
        // 07.10 (limpeza/manutenção de imóveis): só 020201/020202 (exigem o grupo do imóvel).
        var r = RegrasIbsCbs.Resolver("071001", null, null);
        Assert.Null(r.Classificacao);
        Assert.Contains("Emissor Nacional", r.Problema);

        Assert.Contains("não pode ser usado", RegrasIbsCbs.Resolver("010501", "020201", "000001").Problema);
        Assert.Contains("Preencha a classificação", RegrasIbsCbs.Resolver("999999", null, null).Problema);
    }

    [Fact]
    public void Cadastro_do_servico_confere_os_codigos_nas_tabelas()
    {
        Assert.Null(RegrasIbsCbs.ProblemaIndicador(null));
        Assert.Null(RegrasIbsCbs.ProblemaIndicador("100301"));
        Assert.Contains("não encontrado", RegrasIbsCbs.ProblemaIndicador("999999"));
        Assert.Contains("não vale para NFS-e", RegrasIbsCbs.ProblemaIndicador("010104"));
        Assert.Contains("ainda não envia", RegrasIbsCbs.ProblemaIndicador("020201"));

        Assert.Null(RegrasIbsCbs.ProblemaClassificacao(null, null));
        Assert.Null(RegrasIbsCbs.ProblemaClassificacao("000001", "100301"));
        Assert.Contains("não encontrada", RegrasIbsCbs.ProblemaClassificacao("123456", null));
        Assert.Contains("deixe-o em branco", RegrasIbsCbs.ProblemaClassificacao("400001", "060101"));
    }

    // ---------- DPS ----------

    private static DpsRequest ComIbsCbs(IbsCbsDps ibsCbs) => DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = "2", IbsCbs = ibsCbs };

    [Fact]
    public void Grupo_com_tributacao_e_valido_no_XSD_oficial_e_vem_por_ultimo()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt009);
        var (xml, _) = builder.Construir(ComIbsCbs(new IbsCbsDps("100301", "000", "000001", ComTributacao: true)));

        var erros = DpsXsdValidator.Validar(xml, DpsXsdValidator.PastaProducaoRestrita20261020);
        Assert.True(erros.Count == 0, "DPS inválida no XSD de Produção Restrita:\n" + string.Join("\n", erros));

        var infDps = XDocument.Parse(xml).Descendants(XName.Get("infDPS", Ns)).Single();
        var grupo = infDps.Elements().Last();
        Assert.Equal("IBSCBS", grupo.Name.LocalName);
        Assert.Equal(["cIndOp", "indDest", "valores"], grupo.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("100301", grupo.Element(XName.Get("cIndOp", Ns))!.Value);
        Assert.Equal("0", grupo.Element(XName.Get("indDest", Ns))!.Value);
        var trib = grupo.Element(XName.Get("valores", Ns))!.Element(XName.Get("trib", Ns))!;
        Assert.Equal(["CST", "cClassTrib", "gIBSCBS"], trib.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("000", trib.Element(XName.Get("CST", Ns))!.Value);
        Assert.Equal("000001", trib.Element(XName.Get("cClassTrib", Ns))!.Value);
    }

    [Fact]
    public void Grupo_sem_tributacao_nao_leva_cIndOp_nem_gIBSCBS_e_e_valido()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt009);
        var (xml, _) = builder.Construir(ComIbsCbs(new IbsCbsDps(null, "400", "400001", ComTributacao: false)));

        Assert.Empty(DpsXsdValidator.Validar(xml, DpsXsdValidator.PastaProducaoRestrita20261020));
        Assert.DoesNotContain("cIndOp", xml);
        Assert.DoesNotContain("gIBSCBS", xml);
        Assert.Contains("<CST>400</CST>", xml);
    }

    [Fact]
    public void Leiaute_NT004_nunca_leva_o_grupo_e_sem_classificacao_tambem_nao()
    {
        var (nt004, _) = DpsBuilderFactory.Criar(LeiauteDps.Nt004).Construir(ComIbsCbs(new IbsCbsDps("100301", "000", "000001", true)));
        Assert.DoesNotContain("IBSCBS", nt004);

        var (semGrupo, _) = DpsBuilderFactory.Criar(LeiauteDps.Nt009).Construir(DpsRequestFactory.CriarRequestDeTeste());
        Assert.DoesNotContain("IBSCBS", semGrupo);
    }
}
