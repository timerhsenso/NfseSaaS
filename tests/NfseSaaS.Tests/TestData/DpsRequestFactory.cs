using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Tests.TestData;

/// <summary>Fábrica de DpsRequest de teste, reaproveitada entre DpsBuilderTests e DpsSignerTests.</summary>
internal static class DpsRequestFactory
{
    public static DpsRequest CriarRequestDeTeste() => new(
        Prestador: new PrestadorDps(
            Cnpj: "04747304000178",
            InscricaoMunicipal: "366293",
            Telefone: "7135085171",
            Email: "nfe@teste.com.br",
            CodigoMunicipio: "2919207",
            OpSimpNac: "3",
            RegApTribSN: "1",
            RegEspTrib: "0"),
        Tomador: new TomadorDps(
            CnpjOuCpf: "13529565000102",
            Nome: "Cliente Teste",
            CodigoMunicipio: "2927408",
            Cep: "40170120",
            Logradouro: "Rua Teste",
            Numero: "127",
            Bairro: "Centro"),
        Tributacao: new TributacaoDps(
            TribIssqn: "1",
            TpRetIssqn: "1",
            CstPisCofins: "00",
            TpRetPisCofins: "0",
            PercentualTotalTributosSimplesNacional: "3.00"),
        NumeroDps: 1001,
        SerieDps: "00001",
        DataCompetencia: DateOnly.FromDateTime(DateTime.Today),
        Valor: 10.00m,
        CodigoTributacaoNacional: "010701",
        CodigoNbs: "115013000",
        DescricaoServico: "Teste",
        TpAmb: "2");
}