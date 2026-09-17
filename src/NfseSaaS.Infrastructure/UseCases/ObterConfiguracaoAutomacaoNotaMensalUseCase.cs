using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.AutomacaoNotaMensal;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterConfiguracaoAutomacaoNotaMensalUseCase : IObterConfiguracaoAutomacaoNotaMensalUseCase
{
    private readonly AppDbContext _db;

    public ObterConfiguracaoAutomacaoNotaMensalUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ConfiguracaoAutomacaoNotaMensalResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var configuracao = await _db.ConfiguracoesAutomacaoNotaMensal
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId, cancellationToken);

        if (configuracao is null)
        {
            // Default — ver comentário na interface. Nada é gravado aqui;
            // só existe de verdade quando o usuário salvar pela primeira vez.
            return new ConfiguracaoAutomacaoNotaMensalResponse(
                Ativo: false,
                Frequencia: FrequenciaAutomacaoNotaMensal.Mensal,
                DiaSemana: null,
                DiaDoMes: 1,
                Horario: new TimeOnly(9, 0),
                Modo: ModoAutomacaoNotaMensal.ListarParaRevisao,
                DesligadoPorInatividade: false,
                CompetenciasSemConfirmacao: 0);
        }

        return new ConfiguracaoAutomacaoNotaMensalResponse(
            configuracao.Ativo,
            configuracao.Frequencia,
            configuracao.DiaSemana,
            configuracao.DiaDoMes,
            configuracao.Horario,
            configuracao.Modo,
            configuracao.DesligadoPorInatividade,
            configuracao.CompetenciasSemConfirmacao);
    }
}
