using System.Text;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>DOC1: catálogo de documentos, situação/validade, faixas de aviso e conferência do envio.</summary>
public class RegrasDocumentoEmpresaTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 13);
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%teste\n");

    [Fact]
    public void Catalogo_tem_todos_os_tipos_do_enum_uma_vez()
    {
        var doEnum = Enum.GetValues<TipoDocumentoEmpresa>().OrderBy(t => t).ToList();
        Assert.Equal(doEnum, CatalogoDocumentosEmpresa.Todos.Select(t => t.Tipo).OrderBy(t => t).ToList());
        Assert.DoesNotContain(CatalogoDocumentosEmpresa.Fixos, t => t.Tipo == TipoDocumentoEmpresa.Outro);
        Assert.All(CatalogoDocumentosEmpresa.Todos.Where(t => t.LinkEmissao is not null),
            t => Assert.StartsWith("https://", t.LinkEmissao));
    }

    [Fact]
    public void Prazos_usuais_federal_180_fgts_30_cndt_180()
    {
        var emissao = new DateOnly(2026, 11, 1);
        Assert.Equal(emissao.AddDays(180), RegrasDocumentoEmpresa.ValidadeSugerida(TipoDocumentoEmpresa.CndFederal, emissao));
        Assert.Equal(emissao.AddDays(30), RegrasDocumentoEmpresa.ValidadeSugerida(TipoDocumentoEmpresa.CrfFgts, emissao));
        Assert.Equal(emissao.AddDays(180), RegrasDocumentoEmpresa.ValidadeSugerida(TipoDocumentoEmpresa.Cndt, emissao));
        Assert.Null(RegrasDocumentoEmpresa.ValidadeSugerida(TipoDocumentoEmpresa.CndMunicipal, emissao));
        Assert.Null(RegrasDocumentoEmpresa.ValidadeSugerida(TipoDocumentoEmpresa.CrfFgts, null));
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
        var fgts = RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.CrfFgts, null, Hoje, null, null, Hoje);
        Assert.Equal(Hoje.AddDays(30), fgts.ValidaAte);
        Assert.Equal(string.Empty, fgts.NomeChave);

        var municipal = Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.CndMunicipal, null, Hoje, null, null, Hoje));
        Assert.Contains("vale", municipal.Message);

        // Cartão CNPJ não vence: validade em branco é aceita.
        Assert.Null(RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.CartaoCnpj, null, null, null, null, Hoje).ValidaAte);
    }

    [Fact]
    public void Datas_e_textos_conferidos()
    {
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.CndFederal, null, Hoje.AddDays(1), null, null, Hoje));
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.CndEstadual, null, Hoje, Hoje.AddDays(-1), null, Hoje));
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.CartaoCnpj, null, null, null, new string('x', 301), Hoje));

        // Vencida já no envio é aceita (a tela pergunta antes): quem decide é o usuário.
        Assert.Equal(Hoje.AddDays(-5), RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.CndEstadual, null, null, Hoje.AddDays(-5), null, Hoje).ValidaAte);
    }

    [Fact]
    public void Outro_exige_nome_e_agrupa_pela_chave()
    {
        Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.Outro, "  ", null, null, null, Hoje));

        var dados = RegrasDocumentoEmpresa.Validar(TipoDocumentoEmpresa.Outro, "  Alvará   de Funcionamento ", null, null, null, Hoje);
        Assert.Equal("Alvará de Funcionamento", dados.NomePersonalizado);
        Assert.Equal("alvará de funcionamento", dados.NomeChave);
        Assert.Equal(dados.NomeChave, RegrasDocumentoEmpresa.NomeChave(TipoDocumentoEmpresa.Outro, "alvará de funcionamento"));
        Assert.Equal("Alvará de Funcionamento", RegrasDocumentoEmpresa.Titulo(TipoDocumentoEmpresa.Outro, dados.NomePersonalizado));
        Assert.Equal("Cartão CNPJ", RegrasDocumentoEmpresa.Titulo(TipoDocumentoEmpresa.CartaoCnpj, "ignorado"));
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
}
