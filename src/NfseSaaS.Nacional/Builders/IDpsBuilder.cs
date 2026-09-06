using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Builders;

/// <summary>Monta o XML da DPS (Declaração de Prestação de Serviço) a partir dos dados informados.</summary>
public interface IDpsBuilder
{
    /// <returns>O XML da DPS ainda NÃO assinado e o "Id" do elemento infDPS (usado na assinatura).</returns>
    (string XmlDps, string InfDpsId) Construir(DpsRequest request);
}
