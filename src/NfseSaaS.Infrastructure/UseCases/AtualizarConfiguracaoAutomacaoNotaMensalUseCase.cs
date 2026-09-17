using Hangfire;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.AutomacaoNotaMensal;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class AtualizarConfiguracaoAutomacaoNotaMensalUseCase : IAtualizarConfiguracaoAutomacaoNotaMensalUseCase
{
    private readonly AppDbContext _db;
    private readonly IRecurringJobManager _recurringJobManager;

    public AtualizarConfiguracaoAutomacaoNotaMensalUseCase(AppDbContext db, IRecurringJobManager recurringJobManager)
    {
        _db = db;
        _recurringJobManager = recurringJobManager;
    }

    public async Task ExecutarAsync(Guid empresaId, AtualizarConfiguracaoAutomacaoNotaMensalRequest request, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var configuracao = await _db.ConfiguracoesAutomacaoNotaMensal
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId, cancellationToken);

        if (configuracao is null)
        {
            configuracao = new ConfiguracaoAutomacaoNotaMensal { EmpresaId = empresaId };
            _db.ConfiguracoesAutomacaoNotaMensal.Add(configuracao);
        }

        configuracao.Ativo = request.Ativo;
        configuracao.Frequencia = request.Frequencia;
        configuracao.DiaSemana = request.DiaSemana;
        configuracao.DiaDoMes = request.DiaDoMes;
        configuracao.Horario = request.Horario;
        configuracao.Modo = request.Modo;
        configuracao.UpdatedAt = DateTimeOffset.UtcNow;

        // CompetenciasSemConfirmacao/DesligadoPorInatividade NÃO são
        // tocados aqui de propósito — pertencem à Fase 6 (contador de
        // automação), que ainda não existe. Editar a configuração agora
        // não deveria zerar nem mexer num contador que nem existe de
        // verdade ainda.
        await _db.SaveChangesAsync(cancellationToken);

        // Reflete a configuração recém-salva no Hangfire — registra (ou
        // atualiza, se já existia) quando Ativo, remove quando não.
        // AddOrUpdate com o mesmo Id substitui o agendamento anterior
        // em vez de duplicar (ver AutomacaoNotaMensalJobHelper).
        // IRecurringJobManager injetado, não a fachada estática RecurringJob
        // — ver comentário completo em AutomacaoNotaMensalJobSincronizador,
        // mesmo motivo (aqui roda numa requisição HTTP normal, então
        // JobStorage.Current já estaria pronto de qualquer forma, mas é
        // a mesma API em todo lugar que precisa disto, sem excecão).
        var jobId = AutomacaoNotaMensalJobHelper.ObterJobId(empresaId);

        if (configuracao.Ativo)
        {
            _recurringJobManager.AddOrUpdate<IExecutarAutomacaoNotaMensalJob>(
                jobId,
                job => job.ExecutarAsync(empresaId),
                AutomacaoNotaMensalJobHelper.MontarCron(configuracao),
                AutomacaoNotaMensalJobHelper.FusoBrasilia);
        }
        else
        {
            _recurringJobManager.RemoveIfExists(jobId);
        }
    }
}
