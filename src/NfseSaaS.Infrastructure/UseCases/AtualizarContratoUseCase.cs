using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class AtualizarContratoUseCase : IAtualizarContratoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public AtualizarContratoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, AtualizarContratoRequest request, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {id} não encontrado.");

        contrato.Descricao = request.Descricao;
        contrato.DataInicioContrato = request.DataInicioContrato;
        contrato.PeriodicidadeReajusteMeses = request.PeriodicidadeReajusteMeses;
        contrato.IndiceReajuste = request.IndiceReajuste;
        contrato.Observacao = request.Observacao;
        contrato.DiasAlertaOverride = request.DiasAlertaOverride;
        contrato.Status = request.Status;
        contrato.DataFim = request.DataFim;
        contrato.TipoCobranca = request.TipoCobranca;
        contrato.PermitirAlterarValorNaEmissao = request.PermitirAlterarValorNaEmissao;
        contrato.ReajusteAutomatico = request.ReajusteAutomatico;
        contrato.AplicarIndiceNegativo = request.AplicarIndiceNegativo;

        if (request.Servicos is not null)
            await AtualizarServicosAsync(contrato, request, cancellationToken);

        _auditLogWriter.Registrar("AtualizarContrato", "Contrato", contrato.Id, new { contrato.Descricao });

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Aplica a composição de serviços recebida como a NOVA composição
    /// completa do Contrato (diff por ServicoId: atualiza, inclui e remove
    /// linhas) e recalcula ValorAtual = soma das linhas. Notas já emitidas
    /// não mudam (cada Nfse guarda o próprio snapshot).
    ///
    /// Se as linhas mudaram, vira um evento do Histórico de valores — do
    /// tipo que o usuário escolheu (Reajuste ou Alteração de escopo), com
    /// vigência e motivo. Linhas iguais às atuais = nada acontece.
    /// </summary>
    private async Task AtualizarServicosAsync(
        Domain.Entities.Contrato contrato, AtualizarContratoRequest request, CancellationToken cancellationToken)
    {
        var servicos = request.Servicos!;
        var servicoIds = servicos.Select(s => s.ServicoId).Distinct().ToList();

        var linhasAtuais = await _db.ContratoServicos
            .Where(cs => cs.ContratoId == contrato.Id)
            .ToListAsync(cancellationToken);

        var mudou = linhasAtuais.Count != servicos.Count
            || linhasAtuais.Any(a => !servicos.Any(d => d.ServicoId == a.ServicoId && d.Quantidade == a.Quantidade && d.ValorUnitario == a.ValorUnitario));

        if (!mudou)
            return;

        if (request.TipoAlteracaoServicos is not (TipoMovimentacaoValor.Reajuste or TipoMovimentacaoValor.Alteracao))
            throw new RegraNegocioException("Os serviços do contrato mudaram: informe se é um reajuste ou uma alteração de escopo.");

        if (request.VigenciaAlteracaoServicos is not { } vigencia)
            throw new RegraNegocioException("Informe a data de vigência da mudança nos serviços.");

        // Reajuste pelas linhas com um reajuste do aniversário aguardando
        // aprovação seriam dois reajustes concorrentes. Alteração de escopo
        // pode: a aprovação depois aplica o % sobre o valor já alterado.
        if (request.TipoAlteracaoServicos == TipoMovimentacaoValor.Reajuste)
            await ReajusteContrato.GarantirSemPendenteAsync(_db, contrato.Id, cancellationToken);

        var motivo = request.MotivoAlteracaoServicos?.Trim();
        if (request.TipoAlteracaoServicos == TipoMovimentacaoValor.Alteracao && string.IsNullOrEmpty(motivo))
            throw new RegraNegocioException("Informe o motivo da alteração de escopo (ex.: \"inclusão do módulo X\").");

        // Serviços precisam existir e ser da MESMA Empresa do Contrato.
        var encontrados = await _db.Servicos
            .CountAsync(s => servicoIds.Contains(s.Id) && s.EmpresaId == contrato.EmpresaId, cancellationToken);
        if (encontrados != servicoIds.Count)
            throw new RecursoNaoEncontradoException("Um ou mais Serviços informados não foram encontrados nesta Empresa.");

        var valorAnterior = contrato.ValorAtual;

        foreach (var linha in linhasAtuais.Where(l => !servicoIds.Contains(l.ServicoId)))
            _db.ContratoServicos.Remove(linha);

        foreach (var novo in servicos)
        {
            var existente = linhasAtuais.FirstOrDefault(l => l.ServicoId == novo.ServicoId);
            if (existente is not null)
            {
                existente.Quantidade = novo.Quantidade;
                existente.ValorUnitario = novo.ValorUnitario;
            }
            else
            {
                _db.ContratoServicos.Add(new ContratoServico
                {
                    ContratoId = contrato.Id,
                    ServicoId = novo.ServicoId,
                    Quantidade = novo.Quantidade,
                    ValorUnitario = novo.ValorUnitario
                });
            }
        }

        contrato.ValorAtual = servicos.Sum(s => s.Quantidade * s.ValorUnitario);

        // Reajuste feito pelas linhas conta como reajuste do Contrato (move
        // a data do próximo); alteração de escopo não mexe no ciclo.
        if (request.TipoAlteracaoServicos == TipoMovimentacaoValor.Reajuste)
            contrato.DataUltimoReajuste = vigencia;

        _db.MovimentacoesValorContrato.Add(new MovimentacaoValorContrato
        {
            ContratoId = contrato.Id,
            Tipo = request.TipoAlteracaoServicos.Value,
            Status = StatusMovimentacaoValor.Aplicado,
            DataVigencia = vigencia,
            ValorAnterior = valorAnterior,
            ValorNovo = contrato.ValorAtual,
            PercentualAplicado = valorAnterior == 0 ? null : Math.Round((contrato.ValorAtual - valorAnterior) / valorAnterior * 100, 4),
            Indice = request.TipoAlteracaoServicos == TipoMovimentacaoValor.Reajuste ? contrato.IndiceReajuste : null,
            Motivo = string.IsNullOrEmpty(motivo) ? null : motivo,
            ComposicaoJson = await ComposicaoContrato.SerializarAsync(
                _db, servicos.Select(s => (s.ServicoId, s.Quantidade, s.ValorUnitario)), cancellationToken)
        });

        _auditLogWriter.Registrar("AlterarServicosContrato", "Contrato", contrato.Id,
            new { request.TipoAlteracaoServicos, Vigencia = vigencia, ValorAnterior = valorAnterior, ValorNovo = contrato.ValorAtual });
    }
}
