using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.Common;

/// <summary>
/// Calcula, a partir dos dados de um Contrato + o padrão da Empresa, a
/// data prevista do próximo reajuste e a situação atual (EmDia /
/// VencendoEmBreve / Vencido). Puramente funcional — sem banco, sem
/// serviço externo — pra poder ser chamado tanto na listagem quanto no
/// detalhe de Contrato, e futuramente no aviso da tela de emissão (Parte
/// 2) e no card do Dashboard (Parte 4), sem duplicar a conta em cada
/// lugar.
/// </summary>
public static class SituacaoContratoCalculator
{
    public static DateOnly CalcularDataProximoReajuste(Contrato contrato)
    {
        var dataBase = contrato.DataUltimoReajuste ?? contrato.DataInicioContrato;
        return dataBase.AddMonths(contrato.PeriodicidadeReajusteMeses);
    }

    public static int DiasAlertaEfetivo(Contrato contrato, int diasAlertaPadraoDaEmpresa) =>
        contrato.DiasAlertaOverride ?? diasAlertaPadraoDaEmpresa;

    public static SituacaoContrato CalcularSituacao(Contrato contrato, int diasAlertaPadraoDaEmpresa, DateOnly hoje)
    {
        var dataProximoReajuste = CalcularDataProximoReajuste(contrato);

        if (hoje >= dataProximoReajuste)
            return SituacaoContrato.Vencido;

        var diasAlerta = DiasAlertaEfetivo(contrato, diasAlertaPadraoDaEmpresa);
        var dataInicioAlerta = dataProximoReajuste.AddDays(-diasAlerta);

        return hoje >= dataInicioAlerta ? SituacaoContrato.VencendoEmBreve : SituacaoContrato.EmDia;
    }
}
