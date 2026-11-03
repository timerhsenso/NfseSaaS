using NfseSaaS.Application.UseCases.Empresas;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Códigos de regime/tributação do cadastro de Empresa precisam bater com
/// as enumerações do XSD oficial da SEFIN — senão a Empresa fica salva num
/// estado que só quebra na emissão (E1235).
/// </summary>
public class CadastrarEmpresaRequestValidatorCodigosTests
{
    private static readonly CadastrarEmpresaRequestValidator Validator = new();

    [Fact]
    public void Request_valido_deve_passar()
    {
        var resultado = Validator.Validate(RequestValido());

        Assert.True(resultado.IsValid, string.Join("; ", resultado.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("05")]
    [InlineData("49")]
    [InlineData("99")]
    public void Deve_aceitar_CST_de_saida_da_tabela_oficial(string cst)
    {
        var resultado = Validator.Validate(RequestValido() with { CstPisCofins = cst });

        Assert.DoesNotContain(resultado.Errors, e => e.PropertyName == nameof(CadastrarEmpresaRequest.CstPisCofins));
    }

    [Theory]
    [InlineData("50")] // entrada/aquisição — válido no XSD, mas não pra quem emite NFS-e
    [InlineData("10")] // não existe na tabela
    [InlineData("5")]
    public void Deve_reprovar_CST_fora_dos_codigos_de_saida(string cst)
    {
        var resultado = Validator.Validate(RequestValido() with { CstPisCofins = cst });

        Assert.Contains(resultado.Errors, e => e.PropertyName == nameof(CadastrarEmpresaRequest.CstPisCofins));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("8")]
    public void Deve_reprovar_regime_especial_fora_do_XSD(string regEspTrib)
    {
        var resultado = Validator.Validate(RequestValido() with { RegEspTrib = regEspTrib });

        Assert.Contains(resultado.Errors, e => e.PropertyName == nameof(CadastrarEmpresaRequest.RegEspTrib));
    }

    [Theory]
    [InlineData(nameof(CadastrarEmpresaRequest.TribIssqn), "5")]
    [InlineData(nameof(CadastrarEmpresaRequest.TpRetIssqn), "4")]
    [InlineData(nameof(CadastrarEmpresaRequest.TpRetPisCofins), "10")]
    public void Deve_reprovar_codigos_de_tributacao_fora_do_XSD(string campo, string valor)
    {
        var request = campo switch
        {
            nameof(CadastrarEmpresaRequest.TribIssqn) => RequestValido() with { TribIssqn = valor },
            nameof(CadastrarEmpresaRequest.TpRetIssqn) => RequestValido() with { TpRetIssqn = valor },
            _ => RequestValido() with { TpRetPisCofins = valor }
        };

        var resultado = Validator.Validate(request);

        Assert.Contains(resultado.Errors, e => e.PropertyName == campo);
    }

    [Theory]
    [InlineData("(71) 3508-5171", true)]
    [InlineData("7135085171", true)]
    [InlineData("12345", false)]          // menos de 6 dígitos
    [InlineData("(71) ramal", false)]
    public void Telefone_deve_ter_entre_6_e_20_digitos_com_ou_sem_mascara(string telefone, bool valido)
    {
        var resultado = Validator.Validate(RequestValido() with { Telefone = telefone });

        Assert.Equal(valido, !resultado.Errors.Any(e => e.PropertyName == nameof(CadastrarEmpresaRequest.Telefone)));
    }

    private static CadastrarEmpresaRequest RequestValido() => new(
        Cnpj: "04747304000178",
        RazaoSocial: "Empresa Teste",
        NomeFantasia: null,
        InscricaoMunicipal: "366293",
        CodigoMunicipio: "2919207",
        Telefone: "7135085171",
        Email: "nfe@teste.com.br",
        Cep: "42800000",
        Logradouro: "Rua Teste",
        Numero: "1",
        Complemento: null,
        Bairro: "Centro",
        Uf: "BA",
        OpSimpNac: "3",
        RegApTribSN: "1",
        RegEspTrib: "0",
        TribIssqn: "1",
        TpRetIssqn: "1",
        CstPisCofins: "00",
        TpRetPisCofins: "0",
        PercentualTotalTributosSimplesNacional: "3.00");
}
