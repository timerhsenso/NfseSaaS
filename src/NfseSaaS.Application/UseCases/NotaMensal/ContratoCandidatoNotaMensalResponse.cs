namespace NfseSaaS.Application.UseCases.NotaMensal;

/// <summary>
/// Um Contrato elegível (TipoCobranca=Mensal, Status=Ativo) pra entrar no
/// lote da competência pedida. JaEmitidoNestaCompetencia é um backstop de
/// UX (deixa claro na grade antes de confirmar) — a idempotência de
/// verdade é garantida no use case de emissão via IdempotencyKey
/// determinística, não aqui.
/// </summary>
public sealed record ContratoCandidatoNotaMensalResponse(
    Guid ContratoId,
    Guid ClienteId,
    string ClienteNome,
    string Descricao,
    IReadOnlyList<LinhaCandidataNotaMensalResponse> Linhas,
    decimal ValorTotal,
    bool PermitirAlterarValorNaEmissao,
    bool JaEmitidoNestaCompetencia);
