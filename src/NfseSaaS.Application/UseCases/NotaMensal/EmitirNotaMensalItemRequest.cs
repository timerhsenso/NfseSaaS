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
    decimal? ValorTotalAjustado);
