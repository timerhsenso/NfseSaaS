namespace NfseSaaS.Application.UseCases.Recursos;

/// <summary>
/// PL1: ponto único de decisão "essa conta pode X? quanto tem de Y?". Ordem:
/// exceção da conta → plano da assinatura → padrão do catálogo. Nunca é
/// usado para travar emissão de nota (só cadastro e recursos de tela).
/// </summary>
public interface IRecursosConta
{
    Task<RecursoResolvido> ObterAsync(Guid tenantId, string recurso, CancellationToken cancellationToken);

    Task<bool> LigadoAsync(Guid tenantId, string recurso, CancellationToken cancellationToken);

    /// <summary>Barra (422) quando a conta já está no limite; "atual" = quantos já existem.</summary>
    Task GarantirCabeMaisUmAsync(Guid tenantId, string recurso, int atual, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecursoResolvido>> ListarAsync(Guid tenantId, CancellationToken cancellationToken);
}

// ---------- Plataforma › Planos › Recursos ----------

/// <param name="Configurado">false = o plano usa o padrão do catálogo para este recurso.</param>
/// <param name="TextoPadrao">Como funciona sem configurar ("Ilimitado", "Só contas de contador").</param>
public sealed record ItemRecursoPlano(
    string Codigo,
    string Nome,
    string Descricao,
    TipoRecurso Tipo,
    bool Configurado,
    bool? Ligado,
    int? Limite,
    string TextoPadrao);

public sealed record RecursosPlanoResponse(Guid PlanoId, string PlanoNome, IReadOnlyList<ItemRecursoPlano> Itens);

/// <param name="Padrao">true = tira a configuração (volta ao padrão do catálogo).</param>
/// <param name="Limite">Para limite: nulo = ilimitado.</param>
public sealed record ValorRecursoPlanoRequest(string Codigo, bool Padrao, bool? Ligado, int? Limite);

public sealed record SalvarRecursosPlanoRequest(IReadOnlyList<ValorRecursoPlanoRequest> Itens);

// ---------- Plataforma › Contas › Recursos (exceções) ----------

public sealed record ExcecaoRecursoResponse(
    Guid Id,
    string Codigo,
    string Nome,
    TipoRecurso Tipo,
    bool? Ligado,
    int? Limite,
    string Motivo,
    DateOnly? ValidaAte,
    bool Vencida,
    DateTimeOffset CriadaEm,
    string? CriadaPor);

/// <param name="PlanoNome">Plano da assinatura; nulo = sem plano (teste, beta, cobrança desligada).</param>
public sealed record RecursosContaResponse(
    Guid TenantId,
    string? PlanoNome,
    IReadOnlyList<RecursoResolvido> Recursos,
    IReadOnlyList<ExcecaoRecursoResponse> Excecoes);

/// <param name="Limite">Para limite: nulo = ilimitado.</param>
/// <param name="ValidaAte">Último dia da exceção; nulo = sem prazo.</param>
public sealed record SalvarExcecaoRecursoRequest(string Recurso, bool? Ligado, int? Limite, string Motivo, DateOnly? ValidaAte);

/// <summary>Plataforma (só operador). Toda mudança vai para o Histórico do operador.</summary>
public interface IAdministrarRecursosUseCase
{
    Task<RecursosPlanoResponse> RecursosDoPlanoAsync(Guid planoId, CancellationToken cancellationToken);

    Task<RecursosPlanoResponse> SalvarRecursosDoPlanoAsync(Guid planoId, SalvarRecursosPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken);

    Task<RecursosContaResponse> RecursosDaContaAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Cria ou troca a exceção do recurso na conta (uma por recurso).</summary>
    Task<RecursosContaResponse> SalvarExcecaoAsync(Guid tenantId, SalvarExcecaoRecursoRequest request, Guid? operadorId, CancellationToken cancellationToken);

    Task<RecursosContaResponse> RemoverExcecaoAsync(Guid tenantId, Guid excecaoId, Guid? operadorId, CancellationToken cancellationToken);
}
