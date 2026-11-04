namespace NfseSaaS.Domain.Snapshots;

/// <summary>
/// Uma linha de serviço do Contrato congelada num evento do Histórico de
/// valores — "o que era cobrado" naquele momento. Guarda a descrição do
/// Serviço junto (não só o Id) pra continuar legível mesmo que o Serviço
/// seja renomeado ou desativado depois.
/// </summary>
public sealed record ComposicaoContratoLinha(
    Guid ServicoId,
    string ServicoDescricao,
    decimal Quantidade,
    decimal ValorUnitario)
{
    public decimal ValorTotal => Quantidade * ValorUnitario;
}
