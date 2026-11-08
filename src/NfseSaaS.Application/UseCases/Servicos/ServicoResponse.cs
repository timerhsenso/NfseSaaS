using NfseSaaS.Application.UseCases.Catalogos;

namespace NfseSaaS.Application.UseCases.Servicos;

public sealed record ServicoResponse(
    Guid Id,
    Guid EmpresaId,
    string Descricao,
    string CodigoTributacaoNacional,
    string CodigoNbs,
    decimal ValorPadrao,
    bool Ativo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? CIndOp = null,
    string? CClassTrib = null)
{
    /// <summary>
    /// Código de obra/evento que ainda não emitimos (serviço cadastrado
    /// antes do bloqueio): a lista mostra o aviso. Nulo = emite normalmente.
    /// </summary>
    public string? AvisoCodigo => CodigosTributacaoNaoSuportados.Motivo(CodigoTributacaoNacional);
}
