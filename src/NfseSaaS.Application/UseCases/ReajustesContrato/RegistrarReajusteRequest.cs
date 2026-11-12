using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

/// <summary>
/// Reajuste manual. Com um reajuste aplicado há menos de uma periodicidade
/// (ver <see cref="RegrasDesfazerReajuste.ReajusteRecente"/>), o servidor
/// pede uma escolha: <see cref="SubstituirReajusteId"/> desfaz aquele
/// reajuste e aplica este no lugar (o valor novo já vem calculado sobre o
/// valor de antes dele, com <see cref="MotivoSubstituicao"/> como motivo do
/// desfazer); <see cref="ConfirmarReajusteRecente"/> soma mais um reajuste.
/// </summary>
public sealed record RegistrarReajusteRequest(
    DateOnly DataReajuste,
    decimal ValorNovo,
    IndiceReajusteContrato? IndiceUsado,
    string? Observacao,
    Guid? SubstituirReajusteId = null,
    string? MotivoSubstituicao = null,
    bool ConfirmarReajusteRecente = false);
