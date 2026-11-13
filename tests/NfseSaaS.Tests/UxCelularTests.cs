using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// UX2: as regras do celular não podem sumir numa limpeza de CSS/JS sem
/// ninguém perceber (listas em cartões, sem zoom no iPhone, topo sem cortar).
/// </summary>
public class UxCelularTests
{
    private static string Ler(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var caminho = Path.Combine(new[] { dir.FullName, "src", "NfseSaaS.Web", "wwwroot" }.Concat(partes).ToArray());
            if (File.Exists(caminho))
                return File.ReadAllText(caminho);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join('/', partes));
    }

    [Fact]
    public void Listas_viram_cartoes_e_campos_nao_dao_zoom_no_celular()
    {
        var css = Ler("css", "site.css");
        Assert.Contains("table.dataTable:not(.sem-cartoes) thead", css);
        Assert.Contains("content: attr(data-label);", css);
        Assert.Contains("font-size: max(16px, calc(14px * var(--escala-fonte)));", css);
        Assert.Contains(".topbar #seletor-empresa-topo {\n        flex: 1 1 auto;", css.Replace("\r\n", "\n"));

        var js = Ler("js", "site.js");
        Assert.Contains("function rotularCelulasTabela(tabela)", js);
        Assert.Contains("table.dataTable, table.tabela-cartoes", js);
    }
}
