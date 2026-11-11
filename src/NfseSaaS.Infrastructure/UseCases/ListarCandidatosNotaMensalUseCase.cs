using NfseSaaS.Domain.Entities;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarCandidatosNotaMensalUseCase : IListarCandidatosNotaMensalUseCase
{
    private readonly AppDbContext _db;

    public ListarCandidatosNotaMensalUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ContratoCandidatoNotaMensalResponse>> ExecutarAsync(Guid empresaId, DateOnly competencia, CancellationToken cancellationToken)
    {
        // Homologação é homologação, produção é produção: tudo o que esta
        // tela lê das notas (já emitido, último valor, total da última nota)
        // é só do ambiente ATUAL da empresa. A nota guarda o ambiente em que
        // nasceu (Nfse.TipoAmbiente), então teste em homologação nunca marca
        // o mês de produção como resolvido nem vira sugestão de valor.
        var ambiente = await _db.Empresas.Where(e => e.Id == empresaId)
            .Select(e => (TipoAmbiente?)e.TipoAmbiente)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var contratos = await (
            from c in _db.Contratos.AsNoTracking()
            where c.EmpresaId == empresaId && c.TipoCobranca == TipoCobrancaContrato.Mensal && c.Status == StatusContrato.Ativo
            join cli in _db.Clientes.AsNoTracking() on c.ClienteId equals cli.Id
            select new { Contrato = c, ClienteNome = cli.Apelido })
            .ToListAsync(cancellationToken);

        var contratoIds = contratos.Select(x => x.Contrato.Id).ToList();

        var linhasPorContrato = await (
            from cs in _db.ContratoServicos.AsNoTracking()
            where contratoIds.Contains(cs.ContratoId)
            join srv in _db.Servicos.AsNoTracking() on cs.ServicoId equals srv.Id
            select new { cs.ContratoId, Linha = cs, srv.Descricao, srv.CodigoTributacaoNacional })
            .ToListAsync(cancellationToken);

        // "Já emitido nesta competência" olha pro mês inteiro (não o dia
        // exato) — a nota mensal desse contrato pode ter sido emitida em
        // qualquer dia daquele mês, avulsa ou por este mesmo lote antes.
        //
        // Status == Autorizada, de propósito: uma nota Rejeitada (ou
        // Cancelada, Substituida, ainda Processando) NÃO significa que
        // o mês já está resolvido — significa que precisa decidir o que
        // fazer (reenviar corrigido, ou não). Contar qualquer status
        // como "já emitido" faria o modo Automático desistir pra sempre
        // de um contrato rejeitado, e faria o modo ListarParaRevisao
        // nunca mostrar de volta algo que precisa de decisão.
        var inicioMes = new DateOnly(competencia.Year, competencia.Month, 1);
        var fimMes = inicioMes.AddMonths(1).AddDays(-1);

        var contratosComNotaNaCompetencia = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.ContratoId != null && contratoIds.Contains(n.ContratoId.Value)
                && n.DataCompetencia >= inicioMes && n.DataCompetencia <= fimMes
                && n.Status == NfseStatus.Autorizada
                && n.TipoAmbiente == ambiente)
            .Select(n => n.ContratoId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var setComNota = contratosComNotaNaCompetencia.ToHashSet();

        var eventosQueAfetam = await _db.MovimentacoesValorContrato.AsNoTracking()
            .Where(m => contratoIds.Contains(m.ContratoId)
                && ((m.Status == StatusMovimentacaoValor.Pendente && m.DataVigencia <= fimMes)
                    || (m.Status == StatusMovimentacaoValor.Aplicado && m.Tipo != TipoMovimentacaoValor.ValorInicial && m.DataVigencia > fimMes)))
            .Select(m => new { m.ContratoId, m.Status, m.DataVigencia })
            .ToListAsync(cancellationToken);

        // Último valor de cada serviço: a nota autorizada mais recente do
        // contrato ANTES desta competência, por código de tributação (cada
        // código vira uma nota). Olha até 13 meses para trás.
        var limiteHistorico = inicioMes.AddMonths(-13);
        var notasAnteriores = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.ContratoId != null && contratoIds.Contains(n.ContratoId.Value)
                && n.Status == NfseStatus.Autorizada
                && n.TipoAmbiente == ambiente
                && n.DataCompetencia < inicioMes && n.DataCompetencia >= limiteHistorico)
            .Select(n => new { ContratoId = n.ContratoId!.Value, n.CodigoTributacaoNacional, n.DataCompetencia, n.CreatedAt, n.ValorServico, n.ComposicaoValoresJson })
            .ToListAsync(cancellationToken);
        var ultimaPorGrupo = notasAnteriores
            .GroupBy(n => (n.ContratoId, n.CodigoTributacaoNacional))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.DataCompetencia).ThenByDescending(n => n.CreatedAt).First());

        // Total da última competência faturada de cada contrato (soma das
        // notas daquele mês, um por código de tributação).
        var ultimoTotalPorContrato = notasAnteriores
            .GroupBy(n => n.ContratoId)
            .ToDictionary(g => g.Key, g =>
            {
                var competencia = g.Max(n => n.DataCompetencia);
                var mes = new DateOnly(competencia.Year, competencia.Month, 1);
                var doMes = g.Where(n => n.DataCompetencia >= mes && n.DataCompetencia < mes.AddMonths(1)).ToList();
                return (Total: doMes.Sum(n => n.ValorServico), Competencia: mes);
            });

        (decimal? Valor, DateOnly? Competencia) Ultimo(Guid contratoId, Guid contratoServicoId, string codigo, int linhasDoMesmoCodigo)
        {
            if (!ultimaPorGrupo.TryGetValue((contratoId, codigo), out var nota))
                return (null, null);
            var item = ComposicaoValoresNfse.Desserializar(nota.ComposicaoValoresJson).FirstOrDefault(i => i.ContratoServicoId == contratoServicoId);
            if (item is not null)
                return (item.Valor, nota.DataCompetencia);
            // Nota sem composição (antiga ou avulsa): o total só vale se o
            // serviço foi o único daquele código no contrato.
            return linhasDoMesmoCodigo == 1 ? (nota.ValorServico, nota.DataCompetencia) : (null, null);
        }

        string? Aviso(Guid contratoId)
        {
            var eventos = eventosQueAfetam.Where(e => e.ContratoId == contratoId).ToList();
            var pendente = eventos.Where(e => e.Status == StatusMovimentacaoValor.Pendente).OrderBy(e => e.DataVigencia).FirstOrDefault();
            if (pendente is not null)
                return $"Reajuste aguardando aprovação desde {Data(pendente.DataVigencia)} — o valor ainda é o anterior. Decida em Contratos › Histórico de valores.";

            var posterior = eventos.OrderBy(e => e.DataVigencia).FirstOrDefault();
            return posterior is null
                ? null
                : $"O valor já inclui mudança com vigência em {Data(posterior.DataVigencia)}, depois desta competência — confira o valor.";
        }

        static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        return contratos.Select(x =>
        {
            var doContrato = linhasPorContrato.Where(l => l.ContratoId == x.Contrato.Id).ToList();
            var linhas = doContrato
                .Select(l =>
                {
                    var (ultimo, competenciaUltimo) = Ultimo(x.Contrato.Id, l.Linha.Id, l.CodigoTributacaoNacional,
                        doContrato.Count(o => o.CodigoTributacaoNacional == l.CodigoTributacaoNacional));
                    return new LinhaCandidataNotaMensalResponse(
                        l.Linha.ServicoId, l.Descricao, l.CodigoTributacaoNacional, l.Linha.Quantidade, l.Linha.ValorUnitario, l.Linha.ValorTotal,
                        l.Linha.Id, l.Linha.ValorVariavel, ultimo, competenciaUltimo,
                        RegrasValorNotaMensal.Sugestao(l.Linha.ValorTotal, l.Linha.ValorVariavel, ultimo));
                })
                .ToList();

            return new ContratoCandidatoNotaMensalResponse(
                x.Contrato.Id, x.Contrato.ClienteId, x.ClienteNome, NumeroContratoRegras.Rotulo(x.Contrato.Numero, x.Contrato.Descricao),
                linhas, x.Contrato.ValorAtual, x.Contrato.PermitirAlterarValorNaEmissao,
                setComNota.Contains(x.Contrato.Id), Aviso(x.Contrato.Id),
                ultimoTotalPorContrato.TryGetValue(x.Contrato.Id, out var ultimoTotal) ? ultimoTotal.Total : null,
                ultimoTotalPorContrato.TryGetValue(x.Contrato.Id, out var ultimoMes) ? ultimoMes.Competencia : null);
        }).ToList();
    }
}
