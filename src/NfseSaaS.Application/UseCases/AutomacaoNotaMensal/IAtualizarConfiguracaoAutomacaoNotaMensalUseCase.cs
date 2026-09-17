namespace NfseSaaS.Application.UseCases.AutomacaoNotaMensal;

/// <summary>Upsert — cria a linha se a Empresa ainda não tem uma, atualiza se já tem.</summary>
public interface IAtualizarConfiguracaoAutomacaoNotaMensalUseCase
{
    Task ExecutarAsync(Guid empresaId, AtualizarConfiguracaoAutomacaoNotaMensalRequest request, CancellationToken cancellationToken);
}
