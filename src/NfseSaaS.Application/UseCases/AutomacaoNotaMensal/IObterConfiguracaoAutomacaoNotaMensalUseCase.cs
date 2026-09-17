namespace NfseSaaS.Application.UseCases.AutomacaoNotaMensal;

/// <summary>
/// Devolve a configuração da Empresa, ou um DEFAULT sensato (Ativo=false,
/// Mensal, dia 1, 09:00, ListarParaRevisao) se a Empresa ainda não tem
/// nenhuma linha configurada — assim a tela sempre tem algo pra exibir/
/// editar, sem precisar de um fluxo "criar" separado de "editar" (ver
/// IAtualizarConfiguracaoAutomacaoNotaMensalUseCase, que faz upsert).
/// </summary>
public interface IObterConfiguracaoAutomacaoNotaMensalUseCase
{
    Task<ConfiguracaoAutomacaoNotaMensalResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken);
}
