using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>DOC2: formato dos documentos exigidos, avaliação e o que vai no e-mail.</summary>
public class RegrasDocumentosExigidosTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 13);

    private static readonly DocumentoAtual Fgts = new(Guid.NewGuid(), TiposDocumentoSistema.CrfFgts, "", "Regularidade do FGTS (CRF)", "crf.pdf", 1000, Hoje.AddDays(10));
    private static readonly DocumentoAtual Cndt = new(Guid.NewGuid(), TiposDocumentoSistema.Cndt, "", "Certidão trabalhista (CNDT)", "cndt.pdf", 2000, Hoje.AddDays(-3));
    private static readonly DocumentoAtual Alvara = new(Guid.NewGuid(), TiposDocumentoSistema.Outro, "alvará de funcionamento", "Alvará de funcionamento", "alvara.pdf", 500, null);

    private static IReadOnlyList<DocumentoExigidoAvaliado> Itens() => RegrasDocumentosExigidos.Avaliar(new[]
    {
        new ChaveDocumento(TiposDocumentoSistema.CrfFgts, ""),
        new ChaveDocumento(TiposDocumentoSistema.Cndt, ""),
        new ChaveDocumento(TiposDocumentoSistema.CndEstadual, ""),
        new ChaveDocumento(TiposDocumentoSistema.Outro, "alvará de funcionamento")
    }, new[] { Fgts, Cndt, Alvara }, Hoje, CatalogoDocumentosTeste.Sistema);

    [Fact]
    public void Gravar_e_ler_com_outro_normalizado_e_sem_repetir()
    {
        var texto = RegrasDocumentosExigidos.Gravar(new[]
        {
            new ChaveDocumento(TiposDocumentoSistema.CrfFgts, "ignorado"),
            new ChaveDocumento(TiposDocumentoSistema.CrfFgts, ""),
            new ChaveDocumento(TiposDocumentoSistema.Outro, "  Alvará  de Funcionamento ")
        }, CatalogoDocumentosTeste.Sistema);
        Assert.Equal("4;99:alvará de funcionamento", texto);
        Assert.Equal(new[]
        {
            new ChaveDocumento(TiposDocumentoSistema.CrfFgts, ""),
            new ChaveDocumento(TiposDocumentoSistema.Outro, "alvará de funcionamento")
        }, RegrasDocumentosExigidos.Ler(texto));

        Assert.Null(RegrasDocumentosExigidos.Gravar(Array.Empty<ChaveDocumento>(), CatalogoDocumentosTeste.Sistema));
        Assert.Empty(RegrasDocumentosExigidos.Ler(null));
        // Lixo no banco é ignorado (nunca derruba o envio).
        // Tipo que não está no catálogo (77) é lido, mas some na avaliação; "99:" sem nome é descartado.
        Assert.Equal(new[] { new ChaveDocumento(TiposDocumentoSistema.Cndt, ""), new ChaveDocumento(77, "") }, RegrasDocumentosExigidos.Ler("x;5;77;99:"));
        Assert.Single(RegrasDocumentosExigidos.Avaliar(RegrasDocumentosExigidos.Ler("5;77"), Array.Empty<DocumentoAtual>(), Hoje, CatalogoDocumentosTeste.Sistema));
        Assert.Throws<RegraNegocioException>(() => RegrasDocumentosExigidos.Gravar(new[] { new ChaveDocumento(77, "") }, CatalogoDocumentosTeste.Sistema));
        Assert.Throws<RegraNegocioException>(() => RegrasDocumentosExigidos.Gravar(new[] { new ChaveDocumento(TiposDocumentoSistema.Outro, "a") }, CatalogoDocumentosTeste.Sistema));
    }

    [Fact]
    public void Avalia_em_dia_vencido_e_faltando()
    {
        var itens = Itens();
        Assert.Equal(new[]
        {
            SituacaoDocumentoExigido.EmDia, SituacaoDocumentoExigido.Vencido, SituacaoDocumentoExigido.Faltando, SituacaoDocumentoExigido.EmDia
        }, itens.Select(i => i.Situacao));
        Assert.Equal("Certidão estadual", itens[2].Titulo);
        Assert.Equal("Vencido em 10/11/2026", RegrasDocumentosExigidos.TextoSituacao(itens[1]));
        Assert.Equal("Sem validade", RegrasDocumentosExigidos.TextoSituacao(itens[3]));
    }

    [Fact]
    public void Automatico_segue_a_escolha_do_cliente()
    {
        var itens = Itens();

        var semPendente = RegrasDocumentosExigidos.DecidirAutomatico(itens, AcaoDocumentoPendente.MandarSemOPendente);
        Assert.Equal(new[] { Fgts.Id, Alvara.Id }, semPendente.Anexos.Select(a => a.Id));
        Assert.Contains("Vencido, não foi: Certidão trabalhista (CNDT) (venceu em 10/11/2026)", semPendente.Alerta);
        Assert.Contains("Faltando (não enviado em Documentos da empresa): Certidão estadual", semPendente.Alerta);
        Assert.False(semPendente.SegurarEmail);

        var comVencido = RegrasDocumentosExigidos.DecidirAutomatico(itens, AcaoDocumentoPendente.MandarMesmoVencido);
        Assert.Contains(comVencido.Anexos, a => a.Id == Cndt.Id && a.Vencido);
        Assert.StartsWith("Vencido enviado assim mesmo", comVencido.Alerta);

        var segura = RegrasDocumentosExigidos.DecidirAutomatico(itens, AcaoDocumentoPendente.NaoMandarEmail);
        Assert.True(segura.SegurarEmail);
        Assert.Empty(segura.Anexos);

        // Tudo em dia: nada a avisar, nem segurar.
        var emDia = RegrasDocumentosExigidos.Avaliar(new[] { new ChaveDocumento(TiposDocumentoSistema.CrfFgts, "") }, new[] { Fgts }, Hoje, CatalogoDocumentosTeste.Sistema);
        var ok = RegrasDocumentosExigidos.DecidirAutomatico(emDia, AcaoDocumentoPendente.NaoMandarEmail);
        Assert.False(ok.SegurarEmail);
        Assert.Null(ok.Alerta);
        Assert.Single(ok.Anexos);
    }

    [Fact]
    public void Tela_pergunta_pelo_vencido_e_registra_o_retirado()
    {
        var itens = Itens();

        var pergunta = Assert.Throws<RegraNegocioException>(() =>
            RegrasDocumentosExigidos.DecidirManual(itens, new[] { Fgts.Id, Cndt.Id }, enviarVencidos: false));
        Assert.Contains("mandar assim mesmo?", pergunta.Message);

        var confirmado = RegrasDocumentosExigidos.DecidirManual(itens, new[] { Fgts.Id, Cndt.Id }, enviarVencidos: true);
        Assert.Equal(new[] { Fgts.Id, Cndt.Id }, confirmado.Anexos.Select(a => a.Id));
        Assert.Contains("Retirado neste envio: Alvará de funcionamento", confirmado.Alerta);

        // Sem escolha (lote / API sem corpo): os em dia vão; o vencido só com a resposta.
        Assert.Equal(new[] { Fgts.Id, Alvara.Id }, RegrasDocumentosExigidos.DecidirManual(itens, null, false).Anexos.Select(a => a.Id));
        Assert.Equal(3, RegrasDocumentosExigidos.DecidirManual(itens, null, true).Anexos.Count);

        // Id que não é de documento exigido é ignorado.
        Assert.Empty(RegrasDocumentosExigidos.DecidirManual(itens, new[] { Guid.NewGuid() }, false).Anexos);
    }

    [Fact]
    public void Json_do_email_ida_e_volta()
    {
        var decisao = RegrasDocumentosExigidos.DecidirAutomatico(Itens(), AcaoDocumentoPendente.MandarMesmoVencido);
        var json = RegrasDocumentosExigidos.Serializar(decisao.Anexos);
        Assert.Equal(decisao.Anexos, RegrasDocumentosExigidos.Desserializar(json));
        Assert.Null(RegrasDocumentosExigidos.Serializar(Array.Empty<AnexoDocumentoEmail>()));
        Assert.Empty(RegrasDocumentosExigidos.Desserializar("{quebrado"));
    }
}
