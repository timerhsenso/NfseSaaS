using System.Text.RegularExpressions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Aparencia;
using Xunit;

namespace NfseSaaS.Tests;

public class AparenciaTests
{
    [Fact]
    public void Preferencias_vazias_ou_invalidas_viram_o_padrao()
    {
        Assert.Equal(PreferenciasAparencia.Padrao, PreferenciasAparencia.Ler(null));
        Assert.Equal(PreferenciasAparencia.Padrao, PreferenciasAparencia.Ler("não é json"));

        // Opção que saiu do catálogo não quebra: só aquele campo volta ao padrão.
        var lidas = PreferenciasAparencia.Ler("""{"fonte":"xxl","densidade":"compacto","modo":"auto","menuRecolhido":true}""");
        Assert.Equal(new PreferenciasAparencia("m", "compacto", "auto", true), lidas);
    }

    [Fact]
    public void Preferencias_vao_e_voltam_do_json()
    {
        var p = new PreferenciasAparencia("gg", "confortavel", "escuro", true);
        Assert.Equal(p, PreferenciasAparencia.Ler(p.Serializar()));
        Assert.True(p.Serializar().Length <= 300);
    }

    [Theory]
    [InlineData("x", "padrao", "claro", "Tamanho do texto")]
    [InlineData("m", "enorme", "claro", "botões e campos")]
    [InlineData("m", "padrao", "roxo", "Modo")]
    public void Validar_recusa_opcao_fora_do_catalogo(string fonte, string densidade, string modo, string mensagem)
    {
        var erro = Assert.Throws<RegraNegocioException>(() => PreferenciasAparencia.Validar(new PreferenciasAparencia(fonte, densidade, modo, false)));
        Assert.Contains(mensagem, erro.Message);
    }

    [Fact]
    public void Paleta_desconhecida_vira_a_padrao()
    {
        Assert.Equal("mico", AparenciaCatalogo.PaletaOuPadrao(null));
        Assert.Equal("mico", AparenciaCatalogo.PaletaOuPadrao("roxo"));
        Assert.Equal("oceano", AparenciaCatalogo.PaletaOuPadrao("oceano"));
    }

    /// <summary>Catálogo e CSS andam juntos: toda paleta (menos a padrão) tem as regras de claro e escuro; todo tamanho tem a sua.</summary>
    [Fact]
    public void Toda_opcao_do_catalogo_tem_regra_no_css()
    {
        var css = File.ReadAllText(AcharSiteCss());

        foreach (var p in AparenciaCatalogo.Paletas.Where(p => p.Codigo != AparenciaCatalogo.PaletaPadrao))
        {
            Assert.Contains($"html[data-paleta=\"{p.Codigo}\"] {{", css);
            Assert.Contains($"html[data-paleta=\"{p.Codigo}\"][data-theme=\"dark\"] {{", css);
        }
        foreach (var f in AparenciaCatalogo.Fontes.Where(f => f.Codigo != AparenciaCatalogo.FontePadrao))
            Assert.Contains($"html[data-fonte=\"{f.Codigo}\"]", css);
        foreach (var d in AparenciaCatalogo.Densidades.Where(d => d.Codigo != AparenciaCatalogo.DensidadePadrao))
            Assert.Contains($"html[data-densidade=\"{d.Codigo}\"]", css);

        // Nenhum tamanho de fonte fixo escapou da escala (fora as amostras do próprio painel).
        var fixos = Regex.Matches(css, @"font-size:\s*[0-9.]+px").Count;
        Assert.Equal(4, fixos);
    }

    private static string AcharSiteCss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var caminho = Path.Combine(dir.FullName, "src", "NfseSaaS.Web", "wwwroot", "css", "site.css");
            if (File.Exists(caminho))
                return caminho;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("site.css não encontrado a partir de " + AppContext.BaseDirectory);
    }
}
