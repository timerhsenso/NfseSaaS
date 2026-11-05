using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NfseSaaS.Application.UseCases.Cadastro;
using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Certificates;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Regras do cadastro em etapas, pendências da Empresa e CNPJ do certificado.</summary>
public class CadastroEmEtapasRegrasTests
{
    private const string CnpjValido = "04747304000178";
    private const string CpfValido = "52998224725";

    [Fact]
    public void Documento_depende_do_perfil()
    {
        Assert.Null(RegrasCadastroConta.ErroDocumento(PerfilConta.Empresa, "04.747.304/0001-78"));
        Assert.NotNull(RegrasCadastroConta.ErroDocumento(PerfilConta.Empresa, CpfValido));
        Assert.NotNull(RegrasCadastroConta.ErroDocumento(PerfilConta.Empresa, "04747304000179"));

        Assert.Null(RegrasCadastroConta.ErroDocumento(PerfilConta.Contador, CpfValido));
        Assert.Null(RegrasCadastroConta.ErroDocumento(PerfilConta.Contador, CnpjValido));
        Assert.Null(RegrasCadastroConta.ErroDocumento(PerfilConta.Contador, "12ABC34501DE35")); // CNPJ alfanumérico
        Assert.NotNull(RegrasCadastroConta.ErroDocumento(PerfilConta.Contador, "52998224724"));
        Assert.NotNull(RegrasCadastroConta.ErroDocumento((PerfilConta)0, CnpjValido));

        Assert.Equal("12ABC34501DE35", RegrasCadastroConta.NormalizarDocumento("12.abc.345/01de-35"));
        Assert.Equal(CpfValido, RegrasCadastroConta.NormalizarDocumento("529.982.247-25"));
    }

    private static ConsultaCnpjResponse Consulta(string? situacao, bool? simples, bool? mei) => new(
        CnpjValido, "EMPRESA", null, null, null, null, null, null, null, null, null, null, situacao,
        OptanteSimples: simples, OptanteMei: mei);

    [Fact]
    public void Situacao_e_regime_vindos_da_receita()
    {
        Assert.Null(RegrasCadastroConta.ErroSituacao(null));
        Assert.Null(RegrasCadastroConta.ErroSituacao(Consulta("ATIVA", null, null)));
        Assert.Null(RegrasCadastroConta.ErroSituacao(Consulta(null, null, null)));
        Assert.Contains("BAIXADA", RegrasCadastroConta.ErroSituacao(Consulta("BAIXADA", null, null)));

        Assert.Equal("2", Consulta("ATIVA", true, true).OpcaoSimplesSugerida);
        Assert.Equal("3", Consulta("ATIVA", true, false).OpcaoSimplesSugerida);
        Assert.Equal("1", Consulta("ATIVA", false, false).OpcaoSimplesSugerida);
        Assert.Null(Consulta("ATIVA", null, null).OpcaoSimplesSugerida);
    }

    private static Empresa EmpresaCompleta() => new()
    {
        Cnpj = CnpjValido, RazaoSocial = "X", InscricaoMunicipal = "123", CodigoMunicipio = "2927408", Uf = "BA",
        Cep = "40000000", Logradouro = "Rua", Numero = "1", Bairro = "Centro", Telefone = "7133445566", Email = "a@b.com",
        OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "08", TpRetPisCofins = "0",
        PercentualTotalTributosSimplesNacional = "6.00"
    };

    [Fact]
    public void Pendencias_da_empresa()
    {
        Assert.Empty(EmpresaCompleta().PendenciasCadastro());

        var semIm = EmpresaCompleta();
        semIm.InscricaoMunicipal = "";
        Assert.Equal(new[] { "Inscrição Municipal" }, semIm.PendenciasCadastro());

        var simplesSemPercentual = EmpresaCompleta();
        simplesSemPercentual.PercentualTotalTributosSimplesNacional = "";
        Assert.Equal(new[] { "dados fiscais (regime e tributação)" }, simplesSemPercentual.PendenciasCadastro());

        var naoOptante = EmpresaCompleta();
        naoOptante.OpSimpNac = "1";
        naoOptante.PercentualTotalTributosSimplesNacional = "";
        Assert.Empty(naoOptante.PendenciasCadastro());

        var vazia = new Empresa { Cnpj = CnpjValido, RazaoSocial = "X" };
        Assert.Equal(new[] { "Inscrição Municipal", "endereço completo", "telefone", "e-mail", "dados fiscais (regime e tributação)" },
            vazia.PendenciasCadastro());
    }

    // ---------- CNPJ do certificado ----------

    private static X509Certificate2 Certificado(string cn, string? cnpjNoSan)
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (cnpjNoSan is not null)
        {
            // SAN com otherName 2.16.76.1.3.3 (CNPJ, padrão ICP-Brasil) + um e-mail antes, como nos certificados reais.
            var escritor = new AsnWriter(AsnEncodingRules.DER);
            using (escritor.PushSequence())
            {
                escritor.WriteCharacterString(UniversalTagNumber.IA5String, "contato@empresa.com", new Asn1Tag(TagClass.ContextSpecific, 1));
                using (escritor.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    escritor.WriteObjectIdentifier("2.16.76.1.3.3");
                    using (escritor.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                        escritor.WriteOctetString(System.Text.Encoding.ASCII.GetBytes(cnpjNoSan));
                }
            }
            pedido.CertificateExtensions.Add(new X509Extension("2.5.29.17", escritor.Encode(), critical: false));
        }
        return pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    [Fact]
    public void Le_o_cnpj_do_san_icp_brasil_ou_do_nome()
    {
        using var comSan = Certificado("EMPRESA TESTE LTDA", CnpjValido);
        Assert.Equal(CnpjValido, CnpjDoCertificado.Extrair(comSan));

        using var soNoNome = Certificado("EMPRESA TESTE LTDA:04747304000178", null);
        Assert.Equal(CnpjValido, CnpjDoCertificado.Extrair(soNoNome));

        using var semCnpj = Certificado("FULANO DE TAL", null);
        Assert.Null(CnpjDoCertificado.Extrair(semCnpj));
    }

    [Fact]
    public void Aceita_matriz_e_filial_pela_raiz()
    {
        Assert.True(CnpjDoCertificado.MesmaRaiz("04747304000178", "04747304000178"));
        Assert.True(CnpjDoCertificado.MesmaRaiz("04747304000178", "04747304000259"));
        Assert.False(CnpjDoCertificado.MesmaRaiz("04747304000178", "11222333000181"));
    }
}
