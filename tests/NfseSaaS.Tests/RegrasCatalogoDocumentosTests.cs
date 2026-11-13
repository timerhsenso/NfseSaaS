using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>DOC4: links oficiais (ordem, validação, sugestão) e conferências do catálogo e da escolha.</summary>
public class RegrasCatalogoDocumentosTests
{
    private static TipoDocumentoInfo T(int tipo) => CatalogoDocumentosTeste.Sistema.Obter(tipo);

    [Theory]
    [InlineData("https://www.sefaz.ba.gov.br/certidao", "https://www.sefaz.ba.gov.br/certidao")]
    [InlineData("  www.prefeitura.com.br/cnd ", "https://www.prefeitura.com.br/cnd")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Link_aceito_vira_https(string? digitado, string? esperado)
    {
        Assert.Equal(esperado, RegrasLinkDocumento.Normalizar(digitado));
    }

    [Theory]
    [InlineData("http://www.sefaz.ba.gov.br")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://localhost/x")]
    [InlineData("https://usuario:senha@site.com.br")]
    [InlineData("ftp://site.com.br")]
    public void Link_fora_do_padrao_e_recusado(string digitado)
    {
        Assert.Throws<RegraNegocioException>(() => RegrasLinkDocumento.Normalizar(digitado));
    }

    [Fact]
    public void Ordem_do_link_empresa_lugar_sugestao_catalogo()
    {
        var municipal = T(TiposDocumentoSistema.CndMunicipal);
        var estadual = T(TiposDocumentoSistema.CndEstadual);
        var federal = T(TiposDocumentoSistema.CndFederal);

        var proprio = RegrasLinkDocumento.Resolver(municipal, "https://meu.link.com.br", "https://oficial.gov.br", null, "https://sugerido.gov.br", "Salvador");
        Assert.Equal(OrigemLinkDocumento.Empresa, proprio!.Origem);

        var doMunicipio = RegrasLinkDocumento.Resolver(municipal, null, "https://oficial.gov.br", "https://estado.gov.br", "https://sugerido.gov.br", "Salvador");
        Assert.Equal(OrigemLinkDocumento.Municipio, doMunicipio!.Origem);
        Assert.Equal("oficial.gov.br", doMunicipio.Dominio);

        var sugerido = RegrasLinkDocumento.Resolver(municipal, null, null, "https://estado.gov.br", "https://sugerido.gov.br", "Salvador");
        Assert.Equal(OrigemLinkDocumento.Sugerido, sugerido!.Origem); // link de estado não serve para documento municipal
        Assert.Contains("Salvador", sugerido.Texto);

        Assert.Equal(OrigemLinkDocumento.Estado, RegrasLinkDocumento.Resolver(estadual, null, "https://municipio.gov.br", "https://estado.gov.br", null, "BA")!.Origem);
        Assert.Null(RegrasLinkDocumento.Resolver(estadual, null, null, null, null, "BA"));

        // Nacional: o link do catálogo, sem sugestão.
        Assert.Equal(OrigemLinkDocumento.Catalogo, RegrasLinkDocumento.Resolver(federal, null, null, null, "https://outro.gov.br", null)!.Origem);
    }

    [Fact]
    public void Sugestao_so_de_governo_ou_de_varias_contas()
    {
        // Uma conta só, fora de .gov.br: não espalha (evita link plantado).
        Assert.Null(RegrasLinkDocumento.MelhorSugestao(new[] { new UsoLinkDocumento("https://golpe.com.br", 1) }));
        Assert.Equal("https://site.com.br", RegrasLinkDocumento.MelhorSugestao(new[] { new UsoLinkDocumento("https://site.com.br", 2) }));
        Assert.Equal("https://prefeitura.ba.gov.br/cnd", RegrasLinkDocumento.MelhorSugestao(new[]
        {
            new UsoLinkDocumento("https://golpe.com.br", 1),
            new UsoLinkDocumento("https://prefeitura.ba.gov.br/cnd", 1)
        }));
        // Mais contas vence.
        Assert.Equal("https://portal.com.br", RegrasLinkDocumento.MelhorSugestao(new[]
        {
            new UsoLinkDocumento("https://prefeitura.ba.gov.br", 1),
            new UsoLinkDocumento("https://portal.com.br", 3)
        }));
    }

    [Fact]
    public void Padrao_da_empresa_e_conferencia_da_escolha()
    {
        var catalogo = CatalogoDocumentosTeste.Sistema;
        var padrao = RegrasCatalogoDocumentos.PadraoDaEmpresa(catalogo, new[] { TiposDocumentoSistema.Outro, 77 });
        Assert.Equal(8, padrao.Count); // os 8 padrão; "Outro" e tipo desconhecido não entram

        Assert.Equal(new[] { 1, 4 }, RegrasCatalogoDocumentos.ValidarEscolha(new[] { 1, 4, 4 }, catalogo, Array.Empty<int>()));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarEscolha(new[] { TiposDocumentoSistema.Outro }, catalogo, Array.Empty<int>()));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarEscolha(new[] { 77 }, catalogo, Array.Empty<int>()));

        // Inativo: só fica para quem já usava.
        var comInativo = new CatalogoDocumentosEmpresa(catalogo.Todos.Select(t => t.Tipo == 6 ? t with { Ativo = false } : t));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarEscolha(new[] { 6 }, comInativo, Array.Empty<int>()));
        Assert.Equal(new[] { 6 }, RegrasCatalogoDocumentos.ValidarEscolha(new[] { 6 }, comInativo, new[] { 6 }));
        Assert.DoesNotContain(comInativo.Selecionaveis, t => t.Tipo == 6);
    }

    [Fact]
    public void Cadastro_do_operador_conferido()
    {
        SalvarTipoDocumentoRequest Pedido(string nome = "PCMSO", int? dias = 365, string? link = null, bool ativo = true, bool padrao = false) =>
            new(nome, CategoriaDocumento.SaudeOcupacional, AbrangenciaDocumento.Nacional, " Médico ", link, dias, false, ativo, padrao, 10);

        var ok = RegrasCatalogoDocumentos.ValidarTipo(null, Pedido(link: "www.gov.br/pcmso"));
        Assert.Equal("Médico", ok.Orgao);
        Assert.Equal("https://www.gov.br/pcmso", ok.LinkEmissao);
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarTipo(null, Pedido(nome: "ab")));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarTipo(null, Pedido(dias: 0)));
        // "Outro documento" é do sistema: não inativa nem vira padrão.
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarTipo(TiposDocumentoSistema.Outro, Pedido(nome: "Outro documento", ativo: false)));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarTipo(TiposDocumentoSistema.Outro, Pedido(nome: "Outro documento", padrao: true)));

        var catalogo = CatalogoDocumentosTeste.Sistema;
        var pacote = RegrasCatalogoDocumentos.ValidarPacote(new SalvarPacoteDocumentoRequest(" Indústria ", null, new[] { 1, 4, 1 }, true, 1), catalogo);
        Assert.Equal("Indústria", pacote.Nome);
        Assert.Equal(new[] { 1, 4 }, pacote.Tipos);
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarPacote(new SalvarPacoteDocumentoRequest("Vazio", null, Array.Empty<int>(), true, 1), catalogo));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoDocumentos.ValidarPacote(new SalvarPacoteDocumentoRequest("Com outro", null, new[] { 99 }, true, 1), catalogo));
    }
}
