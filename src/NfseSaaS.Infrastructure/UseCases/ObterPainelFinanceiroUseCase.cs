using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Application.UseCases.PainelFinanceiro;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Painel financeiro (DB1): faturamento por mês, receita recorrente, notas a
/// emitir no mês e reajustes chegando. Só leitura, nada novo no banco: tudo
/// sai das notas e dos contratos que já existem (as regras de conta ficam em
/// <see cref="RegrasPainelFinanceiro"/>).
/// </summary>
public sealed class ObterPainelFinanceiroUseCase : IObterPainelFinanceiroUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly ICalcularIndiceAcumuladoUseCase _calcularAcumulado;

    public ObterPainelFinanceiroUseCase(AppDbContext db, ICurrentTenant tenant, ICalcularIndiceAcumuladoUseCase calcularAcumulado)
    {
        _db = db;
        _tenant = tenant;
        _calcularAcumulado = calcularAcumulado;
    }

    public async Task<PainelFinanceiroResponse> ExecutarAsync(PainelFinanceiroRequest request, CancellationToken cancellationToken)
    {
        var hoje = request.Hoje ?? DataBrasilia.Hoje();
        var mesAtual = RegrasPainelFinanceiro.InicioDoMes(hoje);

        var empresas = await _db.Empresas.AsNoTracking()
            .Select(e => new { e.Id, e.RazaoSocial, e.NomeFantasia, e.OpSimpNac, e.TipoAmbiente, e.Ativo })
            .ToListAsync(cancellationToken);

        var perfil = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => (PerfilConta?)t.Perfil)
            .FirstOrDefaultAsync(cancellationToken) ?? PerfilConta.Empresa;

        // Escopo: "só esta empresa" precisa de uma empresa válida da conta;
        // sem ela, vale a soma de todas.
        var empresaAtual = request.EmpresaAtualId is { } idAtual ? empresas.FirstOrDefault(e => e.Id == idAtual) : null;
        var escopo = request.Escopo ?? RegrasPainelFinanceiro.EscopoPadrao(perfil);
        if (empresaAtual is null)
            escopo = EscopoPainelFinanceiro.Todas;
        var empresaId = escopo == EscopoPainelFinanceiro.Empresa ? empresaAtual!.Id : (Guid?)null;
        var empresasNoEscopo = empresaId is null ? empresas : empresas.Where(e => e.Id == empresaId).ToList();
        // Uma empresa só (por escolha ou porque a conta tem uma): dá para falar do limite do regime dela.
        var unica = empresasNoEscopo.Count == 1 ? empresasNoEscopo[0] : null;

        // ---- Notas: produção, autorizadas, pela competência ----
        var inicioJanela = RegrasPainelFinanceiro.InicioJanela(hoje);
        var fimMes = RegrasPainelFinanceiro.FimDoMes(hoje);
        var consultaNotas = _db.NotasFiscais.AsNoTracking()
            .Where(n => n.TipoAmbiente == TipoAmbiente.Producao
                && n.Status == NfseStatus.Autorizada
                && n.DataCompetencia >= inicioJanela && n.DataCompetencia <= fimMes);
        if (empresaId is not null)
            consultaNotas = consultaNotas.Where(n => n.EmpresaId == empresaId);
        var notas = (await consultaNotas
                .Select(n => new { n.DataCompetencia, n.ClienteId, DeContrato = n.ContratoId != null, n.ValorServico })
                .ToListAsync(cancellationToken))
            .Select(n => new NotaPainel(n.DataCompetencia, n.ClienteId, n.DeContrato, n.ValorServico))
            .ToList();

        var meses = RegrasPainelFinanceiro.PorMes(notas, hoje);
        var inicioGrafico = meses[0].Competencia;
        var notasDoGrafico = notas.Where(n => n.Competencia >= inicioGrafico).ToList();
        var inicioAno = new DateOnly(hoje.Year, 1, 1);
        var doAno = notas.Where(n => n.Competencia >= inicioAno).ToList();
        var acumuladoAno = doAno.Sum(n => n.Valor);
        var ultimos12 = RegrasPainelFinanceiro.ReceitaUltimos12Meses(notas, hoje);
        var mesAnterior = mesAtual.AddMonths(-1);

        var idsClientes = notasDoGrafico.Select(n => n.ClienteId).Distinct().ToList();
        var nomesClientes = idsClientes.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Clientes.AsNoTracking()
                .Where(c => idsClientes.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Apelido, cancellationToken);
        var maiores = RegrasPainelFinanceiro.Maiores(notasDoGrafico, nomesClientes, out var totalJanela);

        var contratos = request.IncluirContratos
            ? await MontarContratosAsync(empresaId, empresas.ToDictionary(e => e.Id, e => e.TipoAmbiente), hoje, cancellationToken)
            : null;

        var temDados = notas.Count > 0 || (contratos is not null && contratos.ContratosMensais + contratos.ReajustesQuantidade > 0);

        return new PainelFinanceiroResponse(
            escopo,
            empresaId,
            empresaAtual is null ? null : (string.IsNullOrWhiteSpace(empresaAtual.NomeFantasia) ? empresaAtual.RazaoSocial : empresaAtual.NomeFantasia),
            empresas.Count,
            hoje,
            meses[^1],
            meses[^2],
            acumuladoAno,
            doAno.Count,
            ultimos12,
            meses,
            unica is null ? null : RegrasPainelFinanceiro.Limite(unica.OpSimpNac, acumuladoAno, ultimos12, hoje.Year),
            maiores,
            totalJanela,
            empresasNoEscopo.Count(e => e.Ativo && e.TipoAmbiente != TipoAmbiente.Producao),
            contratos,
            temDados);
    }

    private async Task<ContratosPainelResponse> MontarContratosAsync(
        Guid? empresaId, IReadOnlyDictionary<Guid, TipoAmbiente> ambientePorEmpresa, DateOnly hoje, CancellationToken cancellationToken)
    {
        var mesAtual = RegrasPainelFinanceiro.InicioDoMes(hoje);
        var fimMes = RegrasPainelFinanceiro.FimDoMes(hoje);
        var reajustesAte = RegrasPainelFinanceiro.ReajustesAte(hoje);

        var consulta = _db.Contratos.AsNoTracking().Where(c => c.Status == StatusContrato.Ativo);
        if (empresaId is not null)
            consulta = consulta.Where(c => c.EmpresaId == empresaId);
        var ativos = await (
            from c in consulta
            join cli in _db.Clientes.AsNoTracking() on c.ClienteId equals cli.Id
            select new { Contrato = c, Cliente = cli.Apelido })
            .ToListAsync(cancellationToken);

        // ---- Receita recorrente: mensais ativos vigentes hoje (todos os ambientes: é o contrato, não a nota) ----
        var mensais = ativos.Where(x => x.Contrato.TipoCobranca == TipoCobrancaContrato.Mensal).ToList();
        var recorrentes = mensais
            .Where(x => x.Contrato.DataInicioContrato <= hoje && (x.Contrato.DataFim is null || x.Contrato.DataFim >= hoje))
            .ToList();

        // ---- A emitir: mensal vigente no mês, empresa em produção, sem nota autorizada de produção na competência ----
        var candidatos = mensais
            .Where(x => ambientePorEmpresa.GetValueOrDefault(x.Contrato.EmpresaId) == TipoAmbiente.Producao
                && RegrasPainelFinanceiro.VigenteNoMes(x.Contrato.DataInicioContrato, x.Contrato.DataFim, mesAtual))
            .ToList();
        var idsCandidatos = candidatos.Select(x => x.Contrato.Id).ToList();
        var notasDoMes = idsCandidatos.Count == 0
            ? new List<(Guid ContratoId, NfseStatus Status)>()
            : (await _db.NotasFiscais.AsNoTracking()
                .Where(n => n.ContratoId != null && idsCandidatos.Contains(n.ContratoId.Value)
                    && n.TipoAmbiente == TipoAmbiente.Producao
                    && n.DataCompetencia >= mesAtual && n.DataCompetencia <= fimMes
                    && (n.Status == NfseStatus.Autorizada || n.Status == NfseStatus.Rejeitada))
                .Select(n => new { ContratoId = n.ContratoId!.Value, n.Status })
                .ToListAsync(cancellationToken))
                .Select(n => (n.ContratoId, n.Status))
                .ToList();
        var comNota = notasDoMes.Where(n => n.Status == NfseStatus.Autorizada).Select(n => n.ContratoId).ToHashSet();
        var comRejeitada = notasDoMes.Where(n => n.Status == NfseStatus.Rejeitada).Select(n => n.ContratoId).ToHashSet();
        var aEmitir = candidatos
            .Where(x => !comNota.Contains(x.Contrato.Id))
            .Select(x => new ContratoAEmitirResponse(
                x.Contrato.Id, x.Contrato.EmpresaId, x.Cliente,
                NumeroContratoRegras.Rotulo(x.Contrato.Numero, x.Contrato.Descricao),
                x.Contrato.ValorAtual, comRejeitada.Contains(x.Contrato.Id)))
            // Rejeitada primeiro (precisa de decisão), depois os maiores valores.
            .OrderByDescending(x => x.Rejeitada)
            .ThenByDescending(x => x.Valor)
            .ThenBy(x => x.Cliente, StringComparer.CurrentCulture)
            .ToList();

        // ---- Reajustes até o fim do mês que vem ----
        var comReajuste = ativos
            .Where(x => x.Contrato.PeriodicidadeReajusteMeses > 0)
            .Select(x => new { x.Contrato, x.Cliente, Data = SituacaoContratoCalculator.CalcularDataProximoReajuste(x.Contrato) })
            .Where(x => x.Data <= reajustesAte && (x.Contrato.DataFim is null || x.Contrato.DataFim >= x.Data))
            .ToList();
        var idsReajuste = comReajuste.Select(x => x.Contrato.Id).ToList();
        var pendentes = idsReajuste.Count == 0
            ? new Dictionary<Guid, MovimentacaoValorContrato>()
            : (await _db.MovimentacoesValorContrato.AsNoTracking()
                .Where(m => idsReajuste.Contains(m.ContratoId) && m.Status == StatusMovimentacaoValor.Pendente)
                .ToListAsync(cancellationToken))
                .GroupBy(m => m.ContratoId)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.DataVigencia).First());
        var linhas = idsReajuste.Count == 0
            ? new List<ContratoServico>()
            : await _db.ContratoServicos.AsNoTracking().Where(cs => idsReajuste.Contains(cs.ContratoId)).ToListAsync(cancellationToken);

        // Um cálculo de índice por (índice, mês): vários contratos no mesmo mês usam o mesmo acumulado.
        var acumulados = new Dictionary<(IndiceReajusteContrato, DateOnly), (decimal? Percentual, bool PeloUltimo)>();
        async Task<(decimal? Percentual, bool PeloUltimo)> AcumuladoAsync(IndiceReajusteContrato indice, DateOnly data)
        {
            var chave = (indice, RegrasPainelFinanceiro.InicioDoMes(data));
            if (acumulados.TryGetValue(chave, out var pronto))
                return pronto;
            var calculo = await _calcularAcumulado.ExecutarAsync(indice, data, cancellationToken);
            (decimal?, bool) resultado = (calculo.Disponivel ? calculo.PercentualAcumulado : null, false);
            if (resultado.Item1 is null && data > hoje)
            {
                // Índice do aniversário ainda não saiu: estima pelos 12 meses mais recentes publicados.
                var ultimo = await _calcularAcumulado.ExecutarAsync(indice, hoje, cancellationToken);
                resultado = (ultimo.Disponivel ? ultimo.PercentualAcumulado : null, true);
            }
            acumulados[chave] = resultado;
            return resultado;
        }

        var reajustes = new List<ReajustePrevistoResponse>();
        foreach (var x in comReajuste.OrderBy(x => x.Data).ThenBy(x => x.Cliente, StringComparer.CurrentCulture))
        {
            var c = x.Contrato;
            var rotulo = NumeroContratoRegras.Rotulo(c.Numero, c.Descricao);
            var nomeIndice = c.IndiceReajuste is null ? null : RegrasPainelFinanceiro.NomeIndice(c.IndiceReajuste);
            var atrasado = x.Data < hoje;

            if (pendentes.TryGetValue(c.Id, out var pendente))
            {
                reajustes.Add(new(c.Id, c.EmpresaId, x.Cliente, rotulo, pendente.DataVigencia, pendente.DataVigencia < hoje, "aguardando",
                    pendente.Indice is null ? nomeIndice : RegrasPainelFinanceiro.NomeIndice(pendente.Indice),
                    c.ValorAtual, pendente.ValorNovo, pendente.PercentualAplicado ?? pendente.PercentualCalculado,
                    "Aguardando sua aprovação"));
                continue;
            }

            if (c.IndiceReajuste is null or IndiceReajusteContrato.Outro || c.ValorAtual <= 0)
            {
                reajustes.Add(new(c.Id, c.EmpresaId, x.Cliente, rotulo, x.Data, atrasado, "sem-indice", nomeIndice, c.ValorAtual, null, null,
                    c.IndiceReajuste is null ? "Contrato sem índice: combine o valor com o cliente" : "Índice \"Outro\": informe o valor no contrato"));
                continue;
            }

            var (percentualIndice, peloUltimo) = await AcumuladoAsync(c.IndiceReajuste.Value, x.Data);
            if (percentualIndice is null)
            {
                reajustes.Add(new(c.Id, c.EmpresaId, x.Cliente, rotulo, x.Data, atrasado, "sem-indice", nomeIndice, c.ValorAtual, null, null,
                    "Índice ainda sem os 12 meses publicados"));
                continue;
            }

            var percentual = ReajusteContrato.PercentualEfetivo(percentualIndice.Value, c.AplicarIndiceNegativo);
            var parteVariavel = ReajusteContrato.ParteVariavel(linhas.Where(l => l.ContratoId == c.Id).ToList());
            var valorNovo = ReajusteContrato.CalcularValorNovo(c.ValorAtual, parteVariavel, percentual);
            reajustes.Add(new(c.Id, c.EmpresaId, x.Cliente, rotulo, x.Data, atrasado, "estimado", nomeIndice, c.ValorAtual, valorNovo, percentual,
                peloUltimo ? "Estimado pelo último índice publicado" : null));
        }

        // Impacto: só o que muda a receita recorrente (contratos mensais vigentes).
        var idsRecorrentes = recorrentes.Select(x => x.Contrato.Id).ToHashSet();
        var impacto = reajustes
            .Where(r => r.ValorNovo is not null && idsRecorrentes.Contains(r.ContratoId))
            .Sum(r => r.ValorNovo!.Value - r.ValorAtual);

        return new ContratosPainelResponse(
            recorrentes.Sum(x => x.Contrato.ValorAtual),
            recorrentes.Count,
            aEmitir.Sum(x => x.Valor),
            aEmitir.Count,
            aEmitir.Count(x => x.Rejeitada),
            aEmitir.Take(RegrasPainelFinanceiro.ItensPorLista).ToList(),
            reajustesAte,
            reajustes.Count,
            impacto,
            reajustes.Count(r => r.ValorNovo is null),
            reajustes.Take(RegrasPainelFinanceiro.ItensPorLista).ToList());
    }
}
