using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Application.UseCases.Empresas;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Empresa só vai para produção com certificado válido e aceito pela SEFIN de produção.</summary>
public class RegrasPassagemParaProducaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    private static CertificadoStatusResponse Certificado(DateTimeOffset de, DateTimeOffset ate, bool valido = true) =>
        new(true, "CN=EMPRESA TESTE:12345678000199", de, ate, valido);

    [Fact]
    public void Sem_certificado_nao_vai_para_producao()
    {
        var motivo = RegrasPassagemParaProducao.BloqueioPeloCertificado(new CertificadoStatusResponse(false, null, null, null, false), Agora);
        Assert.NotNull(motivo);
        Assert.Contains("Cadastre o certificado digital", motivo);
        Assert.EndsWith(RegrasPassagemParaProducao.Complemento, motivo);
    }

    [Fact]
    public void Certificado_vencido_ou_que_ainda_nao_vale_nao_vai_para_producao()
    {
        var vencido = RegrasPassagemParaProducao.BloqueioPeloCertificado(Certificado(Agora.AddYears(-1), Agora.AddDays(-1), valido: false), Agora);
        Assert.Contains("venceu em 06/11/2026", vencido);

        var futuro = RegrasPassagemParaProducao.BloqueioPeloCertificado(Certificado(Agora.AddDays(3), Agora.AddYears(1), valido: false), Agora);
        Assert.Contains("só começa a valer em 10/11/2026", futuro);
    }

    [Fact]
    public void Certificado_sem_chave_privada_nao_vai_para_producao()
    {
        var motivo = RegrasPassagemParaProducao.BloqueioPeloCertificado(Certificado(Agora.AddDays(-10), Agora.AddYears(1), valido: false), Agora);
        Assert.Contains("não pode ser usado", motivo);
    }

    [Fact]
    public void Certificado_valido_segue_para_o_teste_na_sefin()
    {
        Assert.Null(RegrasPassagemParaProducao.BloqueioPeloCertificado(Certificado(Agora.AddDays(-10), Agora.AddYears(1)), Agora));
    }

    [Fact]
    public void Teste_na_sefin_de_producao_decide()
    {
        Assert.Null(RegrasPassagemParaProducao.BloqueioPeloTeste(new TesteConexaoResponse(true, "Conexão mTLS estabelecida.")));

        var motivo = RegrasPassagemParaProducao.BloqueioPeloTeste(new TesteConexaoResponse(false, "Certificado recusado no handshake TLS."));
        Assert.NotNull(motivo);
        Assert.Contains("SEFIN Nacional de produção falhou: Certificado recusado no handshake TLS. Confira", motivo);
        Assert.EndsWith(RegrasPassagemParaProducao.Complemento, motivo);
    }
}
