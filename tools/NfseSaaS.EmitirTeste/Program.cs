// ==========================================================================
// NfseSaaS.EmitirTeste
// --------------------------------------------------------------------------
// Ferramenta MANUAL (não roda em "dotnet test") para emitir uma DPS de
// teste através da arquitetura definitiva do SaaS (NfseSaaS.Nacional),
// usando o certificado real já cadastrado via NfseSaaS.CertTool.
//
// Reproduz os mesmos dados de prestador/tomador que a POC validou com
// sucesso (HTTP 201) — objetivo é provar que build → sign → mTLS → SEFIN →
// resposta funcionam de ponta a ponta pela arquitetura nova, não testar
// regra fiscal nova.
//
// Uso:
//   NfseSaaS.EmitirTeste <empresaId-guid> <basePathCertificados> [baseUrlSefin] [ambiente]
//
// Exemplo:
//   NfseSaaS.EmitirTeste 5e844883-0f41-42fd-822d-c7f42261ed23 C:\NfseSaaS-Secrets\certificados
// ==========================================================================

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Nacional;
using NfseSaaS.Nacional.Abstractions;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Services;

if (args.Length < 2)
{
    Console.WriteLine("Uso: NfseSaaS.EmitirTeste <empresaId-guid> <basePathCertificados> [baseUrlSefin] [ambiente]");
    return 1;
}

if (!Guid.TryParse(args[0], out var empresaId))
{
    Console.WriteLine($"ERRO: '{args[0]}' não é um GUID válido de empresaId.");
    return 1;
}

var certBasePath = args[1];
var baseUrl = args.Length > 2 ? args[2] : "https://sefin.producaorestrita.nfse.gov.br/SefinNacional/";
var ambiente = args.Length > 3 ? args[3] : "ProducaoRestrita";

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        // Ferramenta manual recebe uma única URL por execução (o
        // [ambiente] da linha de comando já diz qual é) — preenche as
        // duas chaves com o mesmo valor de propósito; NfseApiClient só
        // vai de fato usar a que corresponder ao tpAmb calculado abaixo.
        ["NfseNacional:BaseUrlHomologacao"] = baseUrl,
        ["NfseNacional:BaseUrlProducao"] = baseUrl,
        ["NfseNacional:TimeoutSeconds"] = "60"
    })
    .Build();

var services = new ServiceCollection();
services.AddNfseNacional(configuration);

// Certificado: mesmo mecanismo do CertTool/Web (Data Protection + DPAPI de
// máquina) — só funciona se "certBasePath" for exatamente o mesmo usado ao
// cadastrar o certificado com o CertTool.
services.AddSingleton<IDataProtectionProvider>(_ =>
    DataProtectionProvider.Create(
        new DirectoryInfo(Path.Combine(certBasePath, "dp-keys")),
        builder =>
        {
            builder.SetApplicationName("NfseSaaS");
            if (OperatingSystem.IsWindows())
                builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }));
services.Configure<CertificateStorageOptions>(o => o.BasePath = certBasePath);
services.AddScoped<CertificateFileStore>();
services.AddScoped<ICertificateProvider, FileCertificateProvider>();

using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var nfseService = scope.ServiceProvider.GetRequiredService<INfseNacionalService>();

// Mesmos dados de prestador/tomador já validados na POC (emissão real
// aceita, HTTP 201) — só o número da DPS muda a cada execução, pra não
// colidir com uma DPS já emitida antes.
var numeroDps = Random.Shared.Next(1000, 999_999);

var request = new DpsRequest(
    Prestador: new PrestadorDps(
        Cnpj: "04747304000178",
        InscricaoMunicipal: "366293",
        Telefone: "7135085171",
        Email: "nfe@rhsenso.com.br",
        CodigoMunicipio: "2919207",
        OpSimpNac: "3",
        RegApTribSN: "1",
        RegEspTrib: "0"),
    Tomador: new TomadorDps(
        CnpjOuCpf: "13529565000102",
        Nome: "CONSELHO REGIONAL DE FARMACIA DO ESTADO DA BAHIA",
        CodigoMunicipio: "2927408",
        Cep: "40170120",
        Logradouro: "DOM BASILIO MENDES RIBEIRO",
        Numero: "127",
        Bairro: "ONDINA"),
    Tributacao: new TributacaoDps(
        TribIssqn: "1",
        TpRetIssqn: "1",
        CstPisCofins: "00",
        TpRetPisCofins: "0",
        PercentualTotalTributosSimplesNacional: "3.00"),
    NumeroDps: numeroDps,
    SerieDps: "00001",
    DataCompetencia: DateOnly.FromDateTime(DateTime.Today),
    Valor: 10.00m,
    CodigoTributacaoNacional: "010701",
    CodigoNbs: "115013000",
    DescricaoServico: "TESTE DE EMISSAO NFS-E VIA NFSESAAS - ARQUITETURA DEFINITIVA",
    // DpsBuilder não lê mais "ambiente" do appsettings/IOptions — quem
    // decide o tpAmb agora é sempre o chamador. Nesta ferramenta manual,
    // isso é o argumento de linha de comando [ambiente] (mesma regra que
    // já era usada: só "Producao" vira tpAmb=1, tudo mais é teste).
    TpAmb: ambiente == "Producao" ? "1" : "2");

Console.WriteLine("==========================================");
Console.WriteLine(" NfseSaaS - Emissão de teste (arquitetura definitiva)");
Console.WriteLine("==========================================");
Console.WriteLine($"Empresa    : {empresaId}");
Console.WriteLine($"Ambiente   : {ambiente}");
Console.WriteLine($"Base URL   : {baseUrl}");
Console.WriteLine($"Número DPS : {numeroDps}");
Console.WriteLine();

try
{
    var resposta = await nfseService.EmitirAsync(request, empresaId, CancellationToken.None);

    Console.WriteLine($"Sucesso      : {resposta.Sucesso}");
    Console.WriteLine($"IdDps        : {resposta.IdDps}");
    Console.WriteLine($"ChaveAcesso  : {resposta.ChaveAcesso}");

    if (resposta.Erros.Count > 0)
    {
        Console.WriteLine("Erros retornados pela SEFIN:");
        foreach (var erro in resposta.Erros)
            Console.WriteLine($"  [{erro.Codigo}] {erro.Descricao}");
    }

    if (resposta.Sucesso && !string.IsNullOrWhiteSpace(resposta.NfseXmlGZipB64))
    {
        var xmlNfse = GZipHelper.DescomprimirDeBase64(resposta.NfseXmlGZipB64);
        var arquivoSaida = $"nfse-{numeroDps}.xml";
        await File.WriteAllTextAsync(arquivoSaida, xmlNfse);
        Console.WriteLine();
        Console.WriteLine($"SUCESSO. XML da NFS-e salvo em: {Path.GetFullPath(arquivoSaida)}");
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("ERRO AO EMITIR:");
    Console.WriteLine(ex);
    return 1;
}

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine(" FIM");
Console.WriteLine("==========================================");

return 0;