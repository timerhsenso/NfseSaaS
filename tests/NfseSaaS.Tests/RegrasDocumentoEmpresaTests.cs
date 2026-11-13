using System.Text;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>DOC1: catálogo de documentos, situação/validade, faixas de aviso e conferência do envio.</summary>
public class RegrasDocumentoEmpresaTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 13);
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%teste\n");

    private static TipoDocumentoInfo T(int tipo) => CatalogoDocumentosTeste.Sistema.Obter(tipo);

    [Fact]
    public void Tipos_de_sistema_mantem_os_numeros_ja_gravados()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 99 }, CatalogoDocumentosTeste.Sistema.Todos.Select(t => t.Tipo).OrderBy(t => t).ToArray());
        Assert.DoesNotContain(CatalogoDocumentosTeste.Sistema.Selecionaveis, t => t.Tipo == TiposDocumentoSistema.Outro);
        Assert.All(CatalogoDocumentosTeste.Sistema.Todos.Where(t => t.LinkEmissao is not null),
            t => Assert.StartsWith("https://", t.LinkEmissao));
        // Os 8 de antes do catálogo continuam vindo marcados para quem ainda não escolheu.
        Assert.Equal(8, CatalogoDocumentosTeste.Sistema.Selecionaveis.Count(t => t.PadraoNovasEmpresas));
        Assert.Equal(AbrangenciaDocumento.Municipal, T(TiposDocumentoSistema.CndMunicipal).Abrangencia);
        Assert.Equal(AbrangenciaDocumento.Estadual, T(TiposDocumentoSistema.CndEstadual).Abrangencia);
    }

    [Fact]
    public void Prazos_usuais_federal_180_fgts_30_cndt_180()
    {
        var emissao = new DateOnly(2026, 11, 1);
        Assert.Equal(emissao.AddDays(180), RegrasDocumentoEmpresa.ValidadeSugerida(T(TiposDocumentoSistema.CndFederal), emissao));
        Assert.Equal(emissao.AddDays(30), RegrasDocumentoEmpresa.ValidadeSugerida(T(TiposDocumentoSistema.CrfFgts), emissao));
        Assert.Equal(emissao.AddDays(180), RegrasDocumentoEmpresa.ValidadeSugerida(T(TiposDocumentoSistema.Cndt), emissao));
        Assert.Null(RegrasDocumentoEmpresa.ValidadeSugerida(T(TiposDocumentoSistema.CndMunicipal), emissao));
        Assert.Null(RegrasDocumentoEmpresa.ValidadeSugerida(T(TiposDocumentoSistema.CrfFgts), null));
    }

    [Theory]
    [InlineData(null, SituacaoDocumentoEmpresa.SemValidade, null)]
    [InlineData(16, SituacaoDocumentoEmpresa.Valido, null)]
    [InlineData(15, SituacaoDocumentoEmpresa.VenceEmBreve, RegrasDocumentoEmpresa.FaixaVenceEmBreve)]
    [InlineData(0, SituacaoDocumentoEmpresa.VenceEmBreve, RegrasDocumentoEmpresa.FaixaVenceEmBreve)]
    [InlineData(-1, SituacaoDocumentoEmpresa.Vencido, RegrasDocumentoEmpresa.FaixaVencido)]
    public void Situacao_e_faixa_de_aviso(int? dias, SituacaoDocumentoEmpresa situacao, int? faixa)
    {
        DateOnly? validade = dias is { } d ? Hoje.AddDays(d) : null;
        Assert.Equal(situacao, RegrasDocumentoEmpresa.Situacao(validade, Hoje));
        Assert.Equal(faixa, RegrasDocumentoEmpresa.FaixaAviso(validade, Hoje));
    }

    [Theory]
    [InlineData(-1, "Venceu ontem")]
    [InlineData(-10, "Vencido há 10 dias")]
    [InlineData(0, "Vence hoje")]
    [InlineData(1, "Vence amanhã")]
    [InlineData(9, "Vence em 9 dias")]
    [InlineData(40, "Válido")]
    public void Texto_da_situacao(int dias, string esperado)
    {
        var validade = Hoje.AddDays(dias);
        Assert.Equal(esperado, RegrasDocumentoEmpresa.TextoSituacao(RegrasDocumentoEmpresa.Situacao(validade, Hoje), dias));
    }

    [Fact]
    public void Certidao_sem_validade_preenche_pelo_prazo_usual_ou_pede_a_data()
    {
        var fgts = RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.CrfFgts), null, Hoje, null, null, Hoje);
        Assert.Equal(Hoje.AddDays(30), fgts.ValidaAte);
        Assert.Equal(string.Empty, fgts.NomeChave);

        var municipal = Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.CndMunicipal), null, Hoje, null, null, Hoje));
        Assert.Contains("vale", municipal.Message);

        // Cartão CNPJ não vence: validade em branco é aceita.
        Assert.Null(RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.CartaoCnpj), null, Hoje, null, null, Hoje).ValidaAte);
    }

    [Fact]
    public void Datas_e_textos_conferidos()
    {
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.CndFederal), null, Hoje.AddDays(1), null, null, Hoje));
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.CndEstadual), null, Hoje, Hoje.AddDays(-1), null, Hoje));
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.CartaoCnpj), null, Hoje, null, new string('x', 301), Hoje));

        // Vencida já no envio é aceita (a tela pergunta antes): quem decide é o usuário.
        Assert.Equal(Hoje.AddDays(-5), RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.CndEstadual), null, Hoje.AddDays(-30), Hoje.AddDays(-5), null, Hoje).ValidaAte);
    }

    [Fact]
    public void Outro_exige_nome_e_agrupa_pela_chave()
    {
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.Outro), "  ", null, null, null, Hoje));

        var dados = RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.Outro), "  Alvará   de Funcionamento ", Hoje, null, null, Hoje);
        Assert.Equal("Alvará de Funcionamento", dados.NomePersonalizado);
        Assert.Equal("alvará de funcionamento", dados.NomeChave);
        Assert.Equal(dados.NomeChave, RegrasDocumentoEmpresa.NomeChave(TiposDocumentoSistema.Outro, "alvará de funcionamento"));
        Assert.Equal("Alvará de Funcionamento", CatalogoDocumentosTeste.Sistema.Titulo(TiposDocumentoSistema.Outro, dados.NomePersonalizado));
        Assert.Equal("Cartão CNPJ", CatalogoDocumentosTeste.Sistema.Titulo(TiposDocumentoSistema.CartaoCnpj, "ignorado"));
    }

    [Fact]
    public void Arquivo_so_pdf_de_verdade_e_ate_5_mb()
    {
        RegrasDocumentoEmpresa.ValidarArquivo("certidao.pdf", Pdf);
        Assert.Throws<RegraNegocioException>(() => RegrasDocumentoEmpresa.ValidarArquivo("vazio.pdf", Array.Empty<byte>()));
        Assert.Throws<RegraNegocioException>(() => RegrasDocumentoEmpresa.ValidarArquivo("foto.pdf", Encoding.ASCII.GetBytes("\x89PNG....")));
        var grande = new byte[RegrasDocumentoEmpresa.TamanhoMaximoBytes + 1];
        Pdf.CopyTo(grande, 0);
        Assert.Throws<RegraNegocioException>(() => RegrasDocumentoEmpresa.ValidarArquivo("grande.pdf", grande));
    }

    [Theory]
    [InlineData("CND Federal.pdf", "CND Federal.pdf")]
    [InlineData(@"C:\pasta\certidão<1>.PDF", "certidão_1.pdf")]
    [InlineData("../../etc/passwd", "passwd.pdf")]
    [InlineData("", "documento.pdf")]
    public void Nome_do_arquivo_fica_seguro(string original, string esperado)
    {
        Assert.Equal(esperado, RegrasDocumentoEmpresa.NomeArquivoSeguro(original));
    }

    [Fact]
    public void Recurso_do_plano_liberado_por_padrao()
    {
        var recurso = CatalogoRecursos.ObterOuErro(CatalogoRecursos.DocumentosEmpresa);
        Assert.Equal(TipoRecurso.LigaDesliga, recurso.Tipo);
        Assert.True(RegrasRecursos.Resolver(recurso, PerfilConta.Empresa, null, null).Ligado);
        Assert.True(RegrasRecursos.Resolver(recurso, PerfilConta.Contador, null, null).Ligado);
    }

    [Fact]
    public void Emissao_obrigatoria_e_validade_por_dias()
    {
        var semEmissao = Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(T(TiposDocumentoSistema.ContratoSocial), null, null, null, null, Hoje));
        Assert.Contains("data de emissão", semEmissao.Message);

        // "Vale por 90 dias" e a data final andam juntos, nos dois sentidos.
        var emissao = new DateOnly(2026, 11, 13);
        Assert.Equal(new DateOnly(2027, 2, 11), RegrasDocumentoEmpresa.ValidadePorDias(emissao, 90));
        Assert.Equal(90, RegrasDocumentoEmpresa.DiasDeValidade(emissao, new DateOnly(2027, 2, 11)));
        Assert.Null(RegrasDocumentoEmpresa.ValidadePorDias(emissao, 0));
        Assert.Null(RegrasDocumentoEmpresa.ValidadePorDias(emissao, null));
        Assert.Null(RegrasDocumentoEmpresa.ValidadePorDias(emissao, RegrasDocumentoEmpresa.ValidadeMaximaDias + 1));
        Assert.Null(RegrasDocumentoEmpresa.DiasDeValidade(emissao, emissao.AddDays(-1)));
        Assert.Null(RegrasDocumentoEmpresa.DiasDeValidade(emissao, null));
    }
}
