namespace NfseSaaS.Application.UseCases.Exportacoes;

/// <summary>
/// Registra, no AuditLog, que um usuário exportou ou imprimiu uma tela —
/// requisito de rastreabilidade: sempre sai registrado quem exportou o
/// quê e quando (data/hora e usuário já vêm de graça no AuditLog, via
/// ICurrentUser e CreatedAt). Nunca grava dado nenhum da grid em si, só
/// o METADADO da ação (tela + formato) — o conteúdo exportado quem tem é
/// o próprio navegador do usuário, isso aqui é só o registro de auditoria.
/// </summary>
public interface IRegistrarExportacaoUseCase
{
    Task ExecutarAsync(string tela, string formato, CancellationToken cancellationToken);
}
