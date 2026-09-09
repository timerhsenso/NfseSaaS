using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Validation;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

public class DpsValidatorTests
{
    private static readonly DpsValidator Validator = new();

    [Fact]
    public void Validar_deve_aprovar_o_request_real_ja_aceito_pela_SEFIN()
    {
        // DpsRequestFactory.CriarRequestDeTeste() reproduz os dados da
        // emissão real (CNPJ 04747304000178, chave 2919207...) documentada
        // como comprovada em homologação — se o validador rejeitar isto,
        // ele está errado, não o dado.
        var request = DpsRequestFactory.CriarRequestDeTeste();

        var erros = Validator.Validar(request);

        Assert.Empty(erros);
    }

    [Fact]
    public void Validar_deve_reprovar_Cnpj_do_prestador_com_digito_verificador_invalido()
    {
        var request = ComPrestador(DpsRequestFactory.CriarRequestDeTeste(), cnpj: "04747304000179");

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("CNPJ do prestador"));
    }

    [Fact]
    public void Validar_deve_reprovar_CnpjOuCpf_do_tomador_invalido()
    {
        var original = DpsRequestFactory.CriarRequestDeTeste();
        var request = original with
        {
            Tomador = original.Tomador with { CnpjOuCpf = "11111111111" }
        };

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("CPF/CNPJ do tomador"));
    }

    [Theory]
    [InlineData("00000")]   // fora da faixa: número zero
    [InlineData("50000")]   // fora da faixa: reservado a tpEmit=2 (portal SEFIN)
    [InlineData("1")]       // formato errado: não tem 5 dígitos
    public void Validar_deve_reprovar_SerieDps_fora_da_faixa_do_aplicativo_proprio(string serieInvalida)
    {
        var original = DpsRequestFactory.CriarRequestDeTeste();
        var request = original with { SerieDps = serieInvalida };

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("SerieDps"));
    }

    [Fact]
    public void Validar_deve_reprovar_Valor_zero_ou_negativo()
    {
        var original = DpsRequestFactory.CriarRequestDeTeste();
        var request = original with { Valor = 0m };

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("Valor do serviço"));
    }

    [Theory]
    [InlineData("0107")]     // 4 dígitos — curto demais
    [InlineData("01070100")] // 8 dígitos — longo demais
    public void Validar_deve_reprovar_CodigoTributacaoNacional_com_tamanho_errado(string codigo)
    {
        var original = DpsRequestFactory.CriarRequestDeTeste();
        var request = original with { CodigoTributacaoNacional = codigo };

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("CodigoTributacaoNacional"));
    }

    [Fact]
    public void Validar_deve_reprovar_CodigoNbs_com_tamanho_errado()
    {
        var original = DpsRequestFactory.CriarRequestDeTeste();
        var request = original with { CodigoNbs = "123" };

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("CodigoNbs"));
    }

    [Fact]
    public void Validar_deve_reprovar_OpSimpNac_fora_dos_3_valores_documentados()
    {
        var request = ComPrestador(DpsRequestFactory.CriarRequestDeTeste(), opSimpNac: "9");

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("OpSimpNac"));
    }

    [Fact]
    public void Validar_deve_exigir_PercentualTotalTributosSimplesNacional_quando_optante_pelo_Simples()
    {
        var original = DpsRequestFactory.CriarRequestDeTeste();
        // Prestador já é OpSimpNac "3" (Optante SN exceto MEI) na factory —
        // aqui só removemos o percentual, que deveria ser obrigatório.
        var request = original with
        {
            Tributacao = original.Tributacao with { PercentualTotalTributosSimplesNacional = "" }
        };

        var erros = Validator.Validar(request);

        Assert.Contains(erros, e => e.Contains("obrigatório para Empresa optante pelo Simples Nacional"));
    }

    [Fact]
    public void Validar_nao_deve_exigir_PercentualTotalTributosSimplesNacional_quando_nao_optante()
    {
        var original = DpsRequestFactory.CriarRequestDeTeste();
        var request = original with
        {
            Prestador = original.Prestador with { OpSimpNac = "1" },
            Tributacao = original.Tributacao with { PercentualTotalTributosSimplesNacional = "" }
        };

        var erros = Validator.Validar(request);

        Assert.DoesNotContain(erros, e => e.Contains("PercentualTotalTributosSimplesNacional"));
    }

    private static DpsRequest ComPrestador(DpsRequest original, string? cnpj = null, string? opSimpNac = null) =>
        original with
        {
            Prestador = original.Prestador with
            {
                Cnpj = cnpj ?? original.Prestador.Cnpj,
                OpSimpNac = opSimpNac ?? original.Prestador.OpSimpNac
            }
        };
}
