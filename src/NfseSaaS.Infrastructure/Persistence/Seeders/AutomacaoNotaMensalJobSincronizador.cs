using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.UseCases.AutomacaoNotaMensal;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Garante que todo RecurringJob do Hangfire bate com o que está salvo
/// em ConfiguracaoAutomacaoNotaMensal (Ativo=true) — roda uma vez no
/// startup, mesmo raciocínio dos outros Seeders desta pasta. Necessário
/// por dois motivos:
///
/// 1. Uma Empresa pode ter salvo Ativo=true numa versão anterior desta
///    automação (Fase 1, antes do Hangfire sequer existir no projeto —
///    Fase 2) e nunca ter reaberto a tela desde então pra "reativar" o
///    registro de verdade. Sem esta reconciliação, a configuração
///    ficaria Ativo=true no banco mas sem nenhum job agendado de fato.
/// 2. Se o schema do Hangfire for perdido ou resetado por qualquer
///    motivo (reprovisionamento do banco, etc.), os jobs recorrentes
///    voltam sozinhos na próxima subida da aplicação, sem precisar de
///    intervenção manual.
///
/// .IgnoreQueryFilters(): roda fora de uma requisição HTTP, sem
/// ICurrentTenant resolvido — precisa enxergar Empresas de TODOS os
/// Tenants, não só um. Mesmo padrão já usado em EmailDispatchHostedService.
/// </summary>
public static class AutomacaoNotaMensalJobSincronizador
{
    public static async Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // IRecurringJobManager via DI, NUNCA a fachada estática RecurringJob
        // aqui — este método roda ANTES de app.Run() (junto dos outros
        // Seeders), e é só quando os hosted services do Hangfire sobem
        // (dentro de app.Run()) que JobStorage.Current fica pronto. A
        // fachada estática depende desse Current; a instância resolvida
        // via DI, não — funciona em qualquer ponto do startup.
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        var configuracoesAtivas = await db.ConfiguracoesAutomacaoNotaMensal
            .IgnoreQueryFilters()
            .Where(c => c.Ativo)
            .ToListAsync();

        foreach (var configuracao in configuracoesAtivas)
        {
            recurringJobManager.AddOrUpdate<IExecutarAutomacaoNotaMensalJob>(
                AutomacaoNotaMensalJobHelper.ObterJobId(configuracao.EmpresaId),
                job => job.ExecutarAsync(configuracao.EmpresaId),
                AutomacaoNotaMensalJobHelper.MontarCron(configuracao),
                AutomacaoNotaMensalJobHelper.FusoBrasilia);
        }
    }
}
