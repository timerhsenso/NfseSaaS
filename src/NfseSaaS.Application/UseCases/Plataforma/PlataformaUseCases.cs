using FluentValidation;

namespace NfseSaaS.Application.UseCases.Plataforma;

/// <summary>Uma conta (Tenant) vista pelo operador da plataforma, com números de uso.</summary>
public sealed record TenantPlataformaResponse(
    Guid Id,
    string RazaoSocial,
    string? NomeFantasia,
    string Documento,
    NfseSaaS.Domain.Enums.PerfilConta Perfil,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset? BloqueadoEm,
    string? MotivoBloqueio,
    string? EmailResponsavel,
    int Usuarios,
    int Empresas,
    int EmpresasEmProducao,
    int NotasNoMes,
    int NotasTotal,
    DateTimeOffset? UltimaNota,
    string? OrigemCadastro,
    string? IndicadaPor,
    int Indicacoes,
    NfseSaaS.Domain.Enums.StatusAssinatura? StatusAssinatura,
    string? Plano,
    decimal? PrecoMensal,
    DateTimeOffset? TesteIniciadoEm,
    CobrancaContaPlataforma? Cobranca = null);

/// <summary>Cobrança da conta vista pelo operador (L6: prorrogar, liberar teste, isentar).</summary>
/// <param name="TesteTerminaEm">Fim do teste com os dias extras (nulo se não começou).</param>
/// <param name="Isenta">Isenta agora, pela configuração da VPS ou pela tela.</param>
/// <param name="IsentaNaConfiguracao">Isenta por Cobranca:ContasIsentas (não dá para mudar pela tela).</param>
/// <param name="TemAssinaturaNoGateway">Assinatura viva no Asaas (não dá para isentar nem prorrogar).</param>
public sealed record CobrancaContaPlataforma(
    DateTimeOffset? TesteTerminaEm,
    int TesteDiasExtras,
    bool SemTesteGratis,
    bool Isenta,
    bool IsentaNaConfiguracao,
    DateOnly? IsentaAte,
    string? MotivoIsencao,
    bool Fundador,
    bool FundadorReservado,
    bool TemAssinaturaNoGateway);

/// <summary>Todas as contas, de todos os tenants — só o operador da plataforma chama.</summary>
public interface IListarTenantsPlataformaUseCase
{
    Task<IReadOnlyList<TenantPlataformaResponse>> ExecutarAsync(CancellationToken cancellationToken);
}

/// <summary>Suspende ou reativa uma conta inteira (login, sessões abertas e emissão).</summary>
public interface IAlterarBloqueioTenantUseCase
{
    Task ExecutarAsync(Guid tenantId, bool suspender, string? motivo, Guid? operadorId, CancellationToken cancellationToken);
}

public sealed record SuspenderTenantRequest(string Motivo);

public sealed class SuspenderTenantRequestValidator : AbstractValidator<SuspenderTenantRequest>
{
    public SuspenderTenantRequestValidator()
    {
        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("Informe o motivo (ex.: inadimplência, pedido do cliente).")
            .MaximumLength(300);
    }
}
