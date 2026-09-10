namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Em que ambiente do SEFIN Nacional as notas de uma Empresa devem ser
/// tratadas como válidas. Valores numéricos iguais ao campo tpAmb da DPS
/// (1 = Produção, 2 = Homologação) — sem tradução em runtime entre este
/// enum e o XML.
///
/// IMPORTANTE: isso é independente de qual URL do SEFIN é usada
/// (NfseNacionalOptions.BaseUrlHomologacao/BaseUrlProducao) — são dois
/// endpoints reais e diferentes, e a escolha de qual chamar em cada
/// requisição é resolvida a partir DESTE enum (via
/// TipoAmbienteExtensions.ParaTpAmb, em Infrastructure), nunca de uma
/// configuração global fixa por deploy.
/// </summary>
public enum TipoAmbiente
{
    Producao = 1,
    Homologacao = 2
}
