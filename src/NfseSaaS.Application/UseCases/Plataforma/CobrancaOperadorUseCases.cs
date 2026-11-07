using FluentValidation;

namespace NfseSaaS.Application.UseCases.Plataforma;

/// <summary>Dias a mais no teste grátis de uma conta (somados aos 30).</summary>
public sealed record ProrrogarTesteRequest(int Dias, string Motivo);

/// <summary>Teste novo de 30 dias a partir de hoje (inclusive para conta sem teste por CNPJ).</summary>
public sealed record LiberarTesteRequest(string Motivo);

/// <summary>Isentar a conta de cobrança até a data (inclusive); sem data = sem prazo.</summary>
public sealed record IsentarContaRequest(DateOnly? Ate, string Motivo);

/// <summary>Resultado das ações de cobrança do operador: a nova situação, para a mensagem da tela.</summary>
public sealed record CobrancaOperadorResponse(string Mensagem);

/// <summary>
/// Ações de cobrança do operador em Plataforma › Contas: prorrogar ou
/// liberar o teste grátis e isentar uma conta pela tela. Tudo vai para o
/// Histórico (auditoria) da conta afetada.
/// </summary>
public interface ICobrancaOperadorUseCase
{
    Task<CobrancaOperadorResponse> ProrrogarTesteAsync(Guid tenantId, ProrrogarTesteRequest request, Guid? operadorId, CancellationToken cancellationToken);
    Task<CobrancaOperadorResponse> LiberarTesteAsync(Guid tenantId, LiberarTesteRequest request, Guid? operadorId, CancellationToken cancellationToken);
    Task<CobrancaOperadorResponse> IsentarAsync(Guid tenantId, IsentarContaRequest request, Guid? operadorId, CancellationToken cancellationToken);
    Task<CobrancaOperadorResponse> RemoverIsencaoAsync(Guid tenantId, Guid? operadorId, CancellationToken cancellationToken);
}

public static class LimitesCobrancaOperador
{
    /// <summary>Por vez.</summary>
    public const int DiasProrrogacaoMaximo = 90;

    /// <summary>Somando todas as prorrogações da conta.</summary>
    public const int DiasExtrasTotalMaximo = 180;
}

public sealed class ProrrogarTesteRequestValidator : AbstractValidator<ProrrogarTesteRequest>
{
    public ProrrogarTesteRequestValidator()
    {
        RuleFor(x => x.Dias)
            .InclusiveBetween(1, LimitesCobrancaOperador.DiasProrrogacaoMaximo)
            .WithMessage($"Dias: de 1 a {LimitesCobrancaOperador.DiasProrrogacaoMaximo}.");
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Informe o motivo.").MaximumLength(300);
    }
}

public sealed class LiberarTesteRequestValidator : AbstractValidator<LiberarTesteRequest>
{
    public LiberarTesteRequestValidator()
    {
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Informe o motivo.").MaximumLength(300);
    }
}

public sealed class IsentarContaRequestValidator : AbstractValidator<IsentarContaRequest>
{
    public IsentarContaRequestValidator()
    {
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Informe o motivo.").MaximumLength(300);
    }
}
