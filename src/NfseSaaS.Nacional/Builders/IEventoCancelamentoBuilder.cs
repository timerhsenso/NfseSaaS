using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Builders;

/// <summary>Monta o XML do evento de cancelamento (e101101) a partir dos dados informados.</summary>
public interface IEventoCancelamentoBuilder
{
    /// <returns>O XML do evento ainda NÃO assinado e o "Id" do elemento infPedReg (usado na assinatura).</returns>
    (string XmlEvento, string InfPedRegId) Construir(EventoCancelamentoRequest request);
}
