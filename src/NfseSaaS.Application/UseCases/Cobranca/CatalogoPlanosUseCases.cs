using FluentValidation;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Plano com o preço que vale hoje (lido pela tela Assinatura e pelo Assinar).</summary>
public sealed record PlanoVigente(
    int Codigo,
    string Nome,
    string Descricao,
    PublicoPlano Publico,
    bool OfertarParaEmpresa,
    bool OfertarParaContador,
    int MaxEmpresas,
    int? NotasPorMes,
    bool AVenda,
    decimal PrecoMensal,
    decimal PrecoFundador);

/// <summary>Leitura do catálogo de planos (banco) para a cobrança.</summary>
public interface ICatalogoPlanos
{
    /// <summary>Planos à venda para o perfil, com preço vigente, na ordem de exibição.</summary>
    Task<IReadOnlyList<PlanoVigente>> PlanosAVendaAsync(PerfilConta perfil, CancellationToken cancellationToken);

    /// <summary>Plano pelo código, com preço vigente (nulo se não existe ou ainda não tem preço).</summary>
    Task<PlanoVigente?> ObterAsync(int codigo, CancellationToken cancellationToken);

    /// <summary>Nome dos planos por código (para listas).</summary>
    Task<IReadOnlyDictionary<int, string>> NomesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Vagas de fundador que sobram no grupo (nunca negativo): total menos
    /// assinaturas fundador e reservas válidas. Zero com a campanha encerrada
    /// pela data (CampanhaAte).
    /// </summary>
    Task<int> VagasFundadorRestantesAsync(PublicoPlano publico, CancellationToken cancellationToken);

    /// <summary>Data-fim da campanha de fundador do grupo (nula = sem data-fim).</summary>
    Task<DateOnly?> CampanhaFundadorAteAsync(PublicoPlano publico, CancellationToken cancellationToken);
}

// ---------- Plataforma › Planos ----------

/// <summary>Linha da tela Plataforma › Planos.</summary>
/// <param name="ProximoPrecoMensal">Preço já agendado para uma data futura (nulo se não há).</param>
/// <param name="Assinantes">Assinaturas não canceladas neste plano.</param>
/// <param name="PodeExcluir">Nunca foi assinado (nem assinatura cancelada).</param>
public sealed record PlanoAdminResponse(
    Guid Id,
    int Codigo,
    string Nome,
    string Descricao,
    PublicoPlano Publico,
    bool OfertarParaEmpresa,
    bool OfertarParaContador,
    int MaxEmpresas,
    int? NotasPorMes,
    bool AVenda,
    int Ordem,
    decimal? PrecoMensal,
    decimal? PrecoFundador,
    DateOnly? VigenteDesde,
    decimal? ProximoPrecoMensal,
    decimal? ProximoPrecoFundador,
    DateOnly? ProximoVigenteDesde,
    int Assinantes,
    bool PodeExcluir);

/// <summary>Criar ou alterar um plano. Preços só na criação; depois, por "Novo preço" (histórico).</summary>
public sealed record SalvarPlanoRequest(
    string Nome,
    string Descricao,
    PublicoPlano Publico,
    bool OfertarParaEmpresa,
    bool OfertarParaContador,
    int MaxEmpresas,
    int? NotasPorMes,
    bool AVenda,
    int Ordem,
    decimal? PrecoMensal,
    decimal? PrecoFundador);

/// <summary>Novo preço a partir de uma data (hoje ou futura). Mesma data de um já cadastrado = correção dele.</summary>
public sealed record NovoPrecoPlanoRequest(decimal PrecoMensal, decimal PrecoFundador, DateOnly VigenteDesde, string? Motivo);

/// <summary>Uma linha do histórico de preços.</summary>
/// <param name="Situacao">"Vigente", "Agendado" ou "Anterior".</param>
public sealed record PrecoPlanoResponse(
    Guid Id,
    decimal PrecoMensal,
    decimal PrecoFundador,
    DateOnly VigenteDesde,
    string? Motivo,
    DateTimeOffset CadastradoEm,
    string? CadastradoPor,
    string Situacao);

/// <param name="Usadas">Assinaturas fundador (inclusive canceladas) mais reservas válidas.</param>
/// <param name="Reservas">Das usadas, quantas são reservas ainda sem assinatura.</param>
/// <param name="CampanhaAte">Último dia da campanha (nulo = sem data-fim).</param>
/// <param name="CampanhaAberta">Aceita novas reservas hoje (sobra vaga e não passou da data).</param>
public sealed record VagasFundadorResponse(PublicoPlano Publico, int Quantidade, int Usadas, int Restantes, int Reservas, DateOnly? CampanhaAte, bool CampanhaAberta);

/// <param name="CampanhaAte">Último dia da campanha do grupo (nulo = sem data-fim).</param>
public sealed record AlterarVagasFundadorRequest(PublicoPlano Publico, int Quantidade, string? Motivo, DateOnly? CampanhaAte = null);

/// <summary>Plataforma › Planos (só operador da plataforma). Toda mudança vai para o Histórico do operador.</summary>
public interface IAdministrarPlanosUseCase
{
    Task<IReadOnlyList<PlanoAdminResponse>> ListarAsync(CancellationToken cancellationToken);

    Task<PlanoAdminResponse> CriarAsync(SalvarPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken);

    Task<PlanoAdminResponse> AlterarAsync(Guid id, SalvarPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrecoPlanoResponse>> HistoricoPrecosAsync(Guid id, CancellationToken cancellationToken);

    Task AdicionarPrecoAsync(Guid id, NovoPrecoPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VagasFundadorResponse>> ListarVagasAsync(CancellationToken cancellationToken);

    Task AlterarVagasAsync(AlterarVagasFundadorRequest request, Guid? operadorId, CancellationToken cancellationToken);
}

// ---------- Excluir plano (código no e-mail do operador) ----------

/// <summary>Para onde foi o código e quantos preços serão apagados junto.</summary>
public sealed record SolicitacaoExclusaoPlanoResponse(string EmailMascarado, int MinutosValidade, string Plano, int Precos);

public sealed record ConfirmarExclusaoPlanoRequest(string Codigo);

public sealed class ConfirmarExclusaoPlanoRequestValidator : AbstractValidator<ConfirmarExclusaoPlanoRequest>
{
    public ConfirmarExclusaoPlanoRequestValidator()
    {
        RuleFor(x => x.Codigo)
            .NotEmpty().WithMessage("Informe o código recebido por e-mail.")
            .Matches("^[0-9]{6}$").WithMessage("O código tem 6 números.");
    }
}

/// <summary>
/// Passo 1: confere se o plano pode ser excluído (nunca assinado) e envia o
/// código ao e-mail do OPERADOR logado.
/// </summary>
public interface ISolicitarExclusaoPlanoUseCase
{
    Task<SolicitacaoExclusaoPlanoResponse> ExecutarAsync(Guid planoId, Guid operadorId, CancellationToken cancellationToken);
}

/// <summary>Passo 2: com o código certo e no prazo, apaga o plano e o histórico de preços dele.</summary>
public interface IConfirmarExclusaoPlanoUseCase
{
    Task ExecutarAsync(Guid planoId, Guid operadorId, string codigo, CancellationToken cancellationToken);
}

/// <summary>Regras do catálogo (sem banco), testadas em CatalogoPlanosRegrasTests.</summary>
public static class RegrasCatalogoPlanos
{
    public const decimal PrecoMaximo = 99_999m;

    /// <summary>
    /// Menor valor que o Asaas aceita numa cobrança (R$ 5,00): abaixo disso
    /// a assinatura e a troca de plano são recusadas pelo gateway.
    /// </summary>
    public const decimal PrecoMinimo = 5m;
    public const int MaxEmpresasLimite = 10_000;
    public const int NotasPorMesLimite = 1_000_000;
    public const int VagasLimite = 10_000;

    /// <summary>Preço que vale na data: o de maior VigenteDesde até ela (nulo se nenhum começou).</summary>
    /// <summary>
    /// Só plano que nunca teve assinatura (nem cancelada, que guarda o código
    /// dele). Plano já assinado sai de venda, nunca do banco.
    /// </summary>
    public static string? MotivoNaoPodeExcluir(int assinaturasJaFeitas) =>
        assinaturasJaFeitas > 0
            ? $"Este plano já foi assinado ({assinaturasJaFeitas} assinatura(s), contando as canceladas). Use \"Fora de venda\"."
            : null;

    public static PrecoPlano? PrecoVigente(IEnumerable<PrecoPlano> precos, DateOnly hoje) =>
        precos.Where(p => p.VigenteDesde <= hoje)
            .OrderByDescending(p => p.VigenteDesde)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();

    /// <summary>Próximo preço agendado (o primeiro com data depois de hoje).</summary>
    public static PrecoPlano? ProximoPreco(IEnumerable<PrecoPlano> precos, DateOnly hoje) =>
        precos.Where(p => p.VigenteDesde > hoje).OrderBy(p => p.VigenteDesde).FirstOrDefault();

    public static string Situacao(PrecoPlano preco, PrecoPlano? vigente, DateOnly hoje) =>
        preco.VigenteDesde > hoje ? "Agendado" : vigente is not null && preco.Id == vigente.Id ? "Vigente" : "Anterior";

    public static void ValidarPlano(SalvarPlanoRequest r, bool criacao)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Nome) || r.Nome.Trim().Length > 60)
            erros.Add("Nome: obrigatório, até 60 caracteres.");
        if ((r.Descricao ?? string.Empty).Trim().Length > 200)
            erros.Add("Descrição: até 200 caracteres.");
        if (!Enum.IsDefined(r.Publico))
            erros.Add("Grupo de fundador inválido.");
        if (r.MaxEmpresas < 1 || r.MaxEmpresas > MaxEmpresasLimite)
            erros.Add($"Empresas incluídas: de 1 a {MaxEmpresasLimite}.");
        if (r.NotasPorMes is { } notas && (notas < 1 || notas > NotasPorMesLimite))
            erros.Add("Notas por mês: de 1 em diante (deixe em branco para sem limite).");
        if (r.AVenda && !r.OfertarParaEmpresa && !r.OfertarParaContador)
            erros.Add("Plano à venda precisa aparecer para empresas, contadores ou ambos.");
        if (r.Ordem < 0 || r.Ordem > 9999)
            erros.Add("Ordem: de 0 a 9999.");
        if (criacao)
        {
            if (r.PrecoMensal is not { } mensal || r.PrecoFundador is not { } fundador)
                erros.Add("Informe o preço normal e o de fundador.");
            else
                erros.AddRange(ErrosPreco(mensal, fundador));
        }

        if (erros.Count > 0)
            throw new RegraNegocioException(string.Join(" ", erros));
    }

    public static void ValidarNovoPreco(NovoPrecoPlanoRequest r, DateOnly hoje)
    {
        var erros = ErrosPreco(r.PrecoMensal, r.PrecoFundador).ToList();
        if (r.VigenteDesde < hoje)
            erros.Add("A data de início não pode ser passada: o preço vale de hoje ou de uma data futura em diante.");
        if ((r.Motivo ?? string.Empty).Trim().Length > 200)
            erros.Add("Motivo: até 200 caracteres.");
        if (erros.Count > 0)
            throw new RegraNegocioException(string.Join(" ", erros));
    }

    public static void ValidarVagas(AlterarVagasFundadorRequest r)
    {
        if (!Enum.IsDefined(r.Publico))
            throw new RegraNegocioException("Grupo de fundador inválido.");
        if (r.Quantidade < 0 || r.Quantidade > VagasLimite)
            throw new RegraNegocioException($"Vagas: de 0 a {VagasLimite}.");
        if (r.CampanhaAte is { } fim && (fim.Year < 2026 || fim.Year > 2100))
            throw new RegraNegocioException("Data-fim da campanha inválida.");
        if ((r.Motivo ?? string.Empty).Trim().Length > 200)
            throw new RegraNegocioException("Motivo: até 200 caracteres.");
    }

    private static IEnumerable<string> ErrosPreco(decimal mensal, decimal fundador)
    {
        if (mensal < PrecoMinimo || mensal > PrecoMaximo || decimal.Round(mensal, 2) != mensal)
            yield return "Preço normal: a partir de R$ 5,00 (mínimo de cobrança do Asaas), com até 2 casas decimais.";
        if (fundador < PrecoMinimo || fundador > PrecoMaximo || decimal.Round(fundador, 2) != fundador)
            yield return "Preço de fundador: a partir de R$ 5,00 (mínimo de cobrança do Asaas), com até 2 casas decimais.";
        if (fundador > mensal)
            yield return "O preço de fundador não pode ser maior que o normal.";
    }
}
