namespace NfseSaaS.Application.UseCases.AutomacaoNotaMensal;

/// <summary>
/// Corpo do job recorrente disparado pelo Hangfire — um job por Empresa
/// (ver AutomacaoNotaMensalJobHelper.ObterJobId), chamado com o
/// EmpresaId de qual delas está rodando agora. Hangfire resolve esta
/// interface via DI a cada disparo (escopo novo por execução, automático
/// com Hangfire.AspNetCore) — não roda dentro de uma requisição HTTP,
/// então nada aqui pode depender de ICurrentTenant/ICurrentUser (ambos
/// resolvidos a partir do HttpContext, indisponível num job).
///
/// Nesta fase (registro do agendamento) o corpo é só um placeholder que
/// prova que o disparo funciona na hora certa — a lógica real (listar
/// candidatos, emitir em lote ou só avisar por e-mail, conforme o Modo
/// configurado) é a fase seguinte da automação.
/// </summary>
public interface IExecutarAutomacaoNotaMensalJob
{
    Task ExecutarAsync(Guid empresaId);
}
