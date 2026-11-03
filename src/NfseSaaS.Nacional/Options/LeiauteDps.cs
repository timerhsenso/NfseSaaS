namespace NfseSaaS.Nacional.Options;

/// <summary>
/// Versão do leiaute (ordem/obrigatoriedade dos elementos) usada pra montar
/// a DPS. NÃO é o atributo <c>versao</c> da DPS — esse continua "1.01" nos
/// dois casos; o que muda é o conteúdo do XSD publicado pra cada ambiente.
///
/// Existe porque a SEFIN publica leiaute novo PRIMEIRO em Produção
/// Restrita (Homologação) e só depois em Produção, então durante a
/// transição os dois ambientes exigem XMLs incompatíveis entre si. Qual
/// leiaute vale pra cada ambiente é configuração
/// (<see cref="NfseNacionalOptions.LeiauteDpsHomologacao"/> /
/// <see cref="NfseNacionalOptions.LeiauteDpsProducao"/>), não código —
/// virar a chave em Produção na data oficial não exige deploy.
/// </summary>
public enum LeiauteDps
{
    /// <summary>
    /// Leiaute da NT 004 v2.0 — vigente em Produção até a entrada da NT 009.
    /// infDPS: tpEmit → cLocEmi. cServ: cTribNac → xDescServ → cNBS.
    /// </summary>
    Nt004 = 0,

    /// <summary>
    /// Leiaute da NT 009 (esquemas de Produção Restrita de 20/10/2026).
    /// infDPS: tpEmit → finNFSe (obrigatório) → cLocEmi.
    /// cServ: cTribNac → cNBS → xDescServ (descrição passou pro final).
    /// </summary>
    Nt009 = 1
}
