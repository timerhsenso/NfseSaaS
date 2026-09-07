namespace NfseSaaS.Application.UseCases.Servicos;

/// <summary>DTO de entrada para cadastro de serviço (implementação na Fase 2).</summary>
public sealed record CadastrarServicoRequest(
    Guid EmpresaId,
    string Descricao,
    string CodigoTributacaoNacional,
    string CodigoNbs,
    decimal ValorPadrao);
