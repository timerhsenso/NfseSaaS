using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Builders;

/// <summary>
/// PLACEHOLDER — Fase 1.
///
/// A lógica de montagem do XML da DPS já foi validada na POC
/// (arquivo DpsBuilder.cs, método Gerar, incluindo namespace do SPED,
/// faixa de série por tpEmit e margem de segurança do dhEmi). Ela será
/// migrada para cá na Fase 2, deliberadamente NÃO reproduzida aqui agora
/// para não ampliar o escopo desta fundação nem duplicar regra fiscal em
/// dois lugares simultaneamente.
/// </summary>
public sealed class DpsBuilder : IDpsBuilder
{
    public (string XmlDps, string InfDpsId) Construir(DpsRequest request)
    {
        throw new NotImplementedException(
            "Migrar de NfsePoc/DpsBuilder.cs (método Gerar) na Fase 2.");
    }
}
