namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record CadastrarContratoRequest(
    Guid EmpresaId,
    Guid ClienteId,
    Guid ServicoId,
    string Descricao,
    decimal ValorAtual,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    string? IndiceReajuste,
    int? DiasAlertaOverride);
