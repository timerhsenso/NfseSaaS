using System.Text.RegularExpressions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Tour;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// TR1: o "já visto" dos tours e a amarração entre o conteúdo (tour.js) e as
/// telas — um id renomeado numa view não pode deixar o tour apontando para o nada
/// sem ninguém perceber.
/// </summary>
public class TourTests
{
    private static DirectoryInfo Web()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var web = new DirectoryInfo(Path.Combine(dir.FullName, "src", "NfseSaaS.Web"));
            if (web.Exists)
                return web;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src/NfseSaaS.Web");
    }

    private static string TourJs() => File.ReadAllText(Path.Combine(Web().FullName, "wwwroot", "js", "tour.js"));

    [Fact]
    public void Marca_le_e_ignora_lixo()
    {
        Assert.Empty(RegrasTour.Ler(null));
        Assert.Equal("painel", RegrasTour.Marcar(null, " Painel "));
        Assert.Equal("painel;notas", RegrasTour.Marcar("painel", "notas"));
        Assert.Equal("painel;notas", RegrasTour.Marcar("painel;notas", "notas"));
        Assert.Equal(new[] { "painel", "notas" }, RegrasTour.Ler("painel;;xyz;notas;painel"));
        Assert.Throws<RegraNegocioException>(() => RegrasTour.Marcar(null, "inexistente"));
        Assert.Throws<RegraNegocioException>(() => RegrasTour.Marcar(null, null));
    }

    [Fact]
    public void Todos_os_tours_vistos_cabem_na_coluna()
    {
        var todos = RegrasTour.Codigos.Aggregate((string?)null, RegrasTour.Marcar);
        Assert.True(todos!.Length <= RegrasTour.TamanhoMaximo);
    }

    [Fact]
    public void Codigos_do_tour_js_batem_com_o_servidor()
    {
        var js = TourJs();
        var bloco = js[js.IndexOf("const TOURS = {", StringComparison.Ordinal)..];
        var codigos = Regex.Matches(bloco, @"^\s{8}'?([a-z-]+)'?: \{\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Equal(RegrasTour.Codigos.OrderBy(c => c), codigos.OrderBy(c => c));
    }

    [Fact]
    public void Ids_apontados_pelo_tour_existem_nas_telas()
    {
        var views = string.Concat(Directory.GetFiles(Path.Combine(Web().FullName, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        var ids = Regex.Matches(TourJs(), @"'#([a-zA-Z][\w-]*)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(ids);
        foreach (var id in ids)
            Assert.True(views.Contains($"id=\"{id}\"", StringComparison.Ordinal), $"O tour aponta para #{id}, que não existe em nenhuma view.");

        // Itens do menu lateral apontados pelo tour.
        var layout = File.ReadAllText(Path.Combine(Web().FullName, "Views", "Shared", "_Layout.cshtml"));
        foreach (var href in Regex.Matches(TourJs(), @"menu\('(/[A-Za-z]+)'\)").Select(m => m.Groups[1].Value).Distinct())
            Assert.Contains($"<a href=\"{href}\"", layout);
    }
}
