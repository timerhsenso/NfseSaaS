namespace NfseSaaS.Application.UseCases.NotaMensal;

/// <summary>
/// ValorTotalAjustado é opcional e só tem efeito se
/// Contrato.PermitirAlterarValorNaEmissao for true — nesse caso, o total
/// informado substitui a soma das linhas SÓ NESTA EMISSÃO (escala cada
/// linha proporcionalmente, mesma técnica já usada em
/// RegistrarReajusteUseCase), sem alterar o Contrato nem virar reajuste
/// permanente.
/// </summary>
public sealed record EmitirNotaMensalItemRequest(
    Guid ContratoId,
    decimal? ValorTotalAjustado,
    // Valor de cada serviço nesta emissão (vence ValorTotalAjustado). Serviço
    // fora da lista = valor do contrato. Ver RegrasValorNotaMensal.
    IReadOnlyList<ValorServicoNotaMensalRequest>? Servicos = null);

/// <param name="ContratoServicoId">Linha do Contrato (ContratoServico.Id).</param>
/// <param name="GravarNoContrato">Serviço de valor fixo: o valor passa a ser o do contrato (Histórico de valores).</param>
public sealed record ValorServicoNotaMensalRequest(Guid ContratoServicoId, decimal Valor, bool GravarNoContrato = false);
