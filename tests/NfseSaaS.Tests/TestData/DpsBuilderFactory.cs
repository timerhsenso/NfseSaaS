using NfseSaaS.Nacional.Builders;
using NfseSaaS.Nacional.Options;

namespace NfseSaaS.Tests.TestData;

/// <summary>
/// Cria DpsBuilder com NfseNacionalOptions de teste. Por padrão usa os
/// MESMOS defaults da classe de options (Homologação = Nt009, Produção =
/// Nt004), pra que o teste reflita o comportamento real sem appsettings.
/// </summary>
internal static class DpsBuilderFactory
{
    public static DpsBuilder Criar(NfseNacionalOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new NfseNacionalOptions()));

    /// <summary>Força o mesmo leiaute nos dois ambientes — útil pra testar um leiaute isoladamente.</summary>
    public static DpsBuilder Criar(LeiauteDps leiaute) =>
        Criar(new NfseNacionalOptions { LeiauteDpsHomologacao = leiaute, LeiauteDpsProducao = leiaute });
}
