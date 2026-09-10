using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Infrastructure;

/// <summary>
/// Ponto único de tradução entre TipoAmbiente (Domain) e o tpAmb (string
/// "1"/"2") que o módulo Nacional espera — Nacional não conhece o
/// Domain, então essa tradução só pode acontecer aqui (Infrastructure),
/// nunca dentro de NfseSaaS.Nacional. Usado por todo use case que chama
/// INfseApiClient/IAdnDistribuicaoClient ou monta um DpsRequest/
/// EventoCancelamentoRequest.
/// </summary>
public static class TipoAmbienteExtensions
{
    public static string ParaTpAmb(this TipoAmbiente ambiente) =>
        ambiente == TipoAmbiente.Producao ? "1" : "2";
}
