using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Application.Fiscal.Retencoes;

/// <summary>C6: leitura e gravação dos percentuais de retenção federal do Cliente.</summary>
public static class RetencoesCliente
{
    public static PercentuaisRetencao De(Cliente cliente) => new(
        cliente.RetencaoIrrfPercentual,
        cliente.RetencaoPisPercentual,
        cliente.RetencaoCofinsPercentual,
        cliente.RetencaoCsllPercentual,
        cliente.RetencaoInssPercentual);

    public static void Aplicar(Cliente cliente, PercentuaisRetencao percentuais)
    {
        cliente.RetencaoIrrfPercentual = percentuais.Irrf;
        cliente.RetencaoPisPercentual = percentuais.Pis;
        cliente.RetencaoCofinsPercentual = percentuais.Cofins;
        cliente.RetencaoCsllPercentual = percentuais.Csll;
        cliente.RetencaoInssPercentual = percentuais.Inss;
    }
}
