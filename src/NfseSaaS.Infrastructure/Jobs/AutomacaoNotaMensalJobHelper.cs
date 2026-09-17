using Hangfire;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Lógica compartilhada entre o registro imediato (ao salvar a
/// configuração — ver AtualizarConfiguracaoAutomacaoNotaMensalUseCase) e
/// a reconciliação no startup (AutomacaoNotaMensalJobSincronizador): as
/// duas precisam montar o mesmo Id de job e a mesma expressão cron a
/// partir da mesma configuração, então fica centralizado aqui em vez de
/// duplicado.
/// </summary>
public static class AutomacaoNotaMensalJobHelper
{
    /// <summary>
    /// Brasília fixo, NUNCA TimeZoneInfo.Local — o servidor roda em UTC
    /// em produção (mesmo motivo do bug antigo do dhEmi na DPS: sem
    /// isto, o job dispara na hora certa em UTC, não na hora que a
    /// Empresa configurou na tela). Os métodos Cron.* do Hangfire geram
    /// expressão avaliada em UTC por padrão — por isso todo AddOrUpdate
    /// precisa passar este fuso explicitamente, nunca confiar no default.
    /// </summary>
    public static readonly TimeZoneInfo FusoBrasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static string ObterJobId(Guid empresaId) => $"nota-mensal-empresa-{empresaId}";

    public static string MontarCron(ConfiguracaoAutomacaoNotaMensal configuracao) => configuracao.Frequencia switch
    {
        FrequenciaAutomacaoNotaMensal.Diaria =>
            Cron.Daily(configuracao.Horario.Hour, configuracao.Horario.Minute),

        // DiaSemana!/DiaDoMes! (null-forgiving): o validador já garante
        // que estes campos vêm preenchidos quando a Frequencia exige
        // (ver AtualizarConfiguracaoAutomacaoNotaMensalRequestValidator)
        // — se chegou até aqui com Semanal/Mensal, o campo existe.
        FrequenciaAutomacaoNotaMensal.Semanal =>
            Cron.Weekly(configuracao.DiaSemana!.Value, configuracao.Horario.Hour, configuracao.Horario.Minute),

        FrequenciaAutomacaoNotaMensal.Mensal =>
            Cron.Monthly(configuracao.DiaDoMes!.Value, configuracao.Horario.Hour, configuracao.Horario.Minute),

        _ => throw new InvalidOperationException($"Frequência {configuracao.Frequencia} não tratada em MontarCron.")
    };
}
