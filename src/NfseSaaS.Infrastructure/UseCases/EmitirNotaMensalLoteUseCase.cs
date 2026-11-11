using Microsoft.EntityFrameworkCore;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Processa o lote SEQUENCIALMENTE, um Contrato/grupo por vez — nunca em
/// paralelo (mesmo certificado mTLS compartilhado por Empresa, e lote
/// mensal não tem urgência de milissegundos) e sem transação guarda-chuva
/// (cada Nfse já é sua própria unidade, mesma lógica de
/// EmitirNfseUseCase). Uma falha isolada NUNCA aborta o restante do lote
/// — cada item segue seu próprio destino e o resultado completo (sucesso
/// e falha lado a lado) volta numa lista só pro chamador decidir o que
/// reprocessar.
/// </summary>
public sealed class EmitirNotaMensalLoteUseCase : IEmitirNotaMensalLoteUseCase
{
    private readonly AppDbContext _db;
    private readonly IEmitirNfseUseCase _emitirNfse;
    private readonly IAuditLogWriter _auditLogWriter;

    public EmitirNotaMensalLoteUseCase(AppDbContext db, IEmitirNfseUseCase emitirNfse, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _emitirNfse = emitirNfse;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<IReadOnlyList<ItemResultadoNotaMensalResponse>> ExecutarAsync(EmitirNotaMensalLoteRequest request, CancellationToken cancellationToken)
    {
        var resultados = new List<ItemResultadoNotaMensalResponse>();

        foreach (var item in request.Itens)
        {
            // Cada Contrato do lote é buscado/validado individualmente —
            // um Contrato inválido/excluído entre a listagem de
            // candidatos e a confirmação vira UM resultado com falha,
            // não interrompe os outros.
            var contrato = await _db.Contratos.AsNoTracking().FirstOrDefaultAsync(
                c => c.Id == item.ContratoId && c.EmpresaId == request.EmpresaId, cancellationToken);

            if (contrato is null)
            {
                resultados.Add(new ItemResultadoNotaMensalResponse(item.ContratoId, "—", "—", false, null, null, "Contrato não encontrado para esta Empresa."));
                continue;
            }

            if (contrato.TipoCobranca != TipoCobrancaContrato.Mensal || contrato.Status != StatusContrato.Ativo)
            {
                resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, "—", contrato.Descricao, false, null, null, "Contrato não é mais elegível (precisa ser Mensal e Ativo)."));
                continue;
            }

            var cliente = await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contrato.ClienteId, cancellationToken);
            var clienteNome = cliente?.Apelido ?? "—";

            var linhas = await (
                from cs in _db.ContratoServicos.AsNoTracking()
                where cs.ContratoId == contrato.Id
                join srv in _db.Servicos.AsNoTracking() on cs.ServicoId equals srv.Id
                select new { cs.Id, cs.ServicoId, cs.Quantidade, cs.ValorUnitario, cs.ValorVariavel, srv.Descricao, srv.CodigoTributacaoNacional })
                .ToListAsync(cancellationToken);

            if (linhas.Count == 0)
            {
                resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, clienteNome, contrato.Descricao, false, null, null, "Contrato sem serviços cadastrados."));
                continue;
            }

            // Valor de cada serviço nesta emissão.
            // - Com "Servicos" (tela nova): um valor por linha; variável
            //   muda sempre, fixo só se o contrato permitir ou se for para
            //   gravar no contrato (RegrasValorNotaMensal).
            // - Sem (automação e chamadas antigas): ValorTotalAjustado escala
            //   as linhas proporcionalmente, só nesta emissão, se o contrato
            //   permitir — como sempre foi.
            Dictionary<Guid, decimal> valorPorLinha;
            var contratoAtualizado = false;
            if (item.Servicos is { Count: > 0 })
            {
                IReadOnlyList<ValorDecidido> decididos;
                try
                {
                    decididos = RegrasValorNotaMensal.Decidir(
                        linhas.Select(l => new LinhaContratoValor(l.Id, l.Descricao, l.Quantidade * l.ValorUnitario, l.ValorVariavel)).ToList(),
                        item.Servicos, contrato.PermitirAlterarValorNaEmissao);
                }
                catch (InvalidOperationException ex)
                {
                    resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, clienteNome, contrato.Descricao, false, null, null, ex.Message));
                    continue;
                }

                var paraGravar = decididos.Where(d => d.GravarNoContrato).ToList();
                if (paraGravar.Count > 0)
                {
                    await GravarValoresNoContratoAsync(contrato.Id, paraGravar, request.Competencia, cancellationToken);
                    contratoAtualizado = true;
                }

                valorPorLinha = decididos.ToDictionary(d => d.ContratoServicoId, d => d.Valor);
            }
            else
            {
                var fator = 1m;
                if (item.ValorTotalAjustado.HasValue && contrato.PermitirAlterarValorNaEmissao && contrato.ValorAtual != 0)
                    fator = item.ValorTotalAjustado.Value / contrato.ValorAtual;
                valorPorLinha = linhas.ToDictionary(l => l.Id, l => l.Quantidade * l.ValorUnitario * fator);
            }

            // Agrupa por CodigoTributacaoNacional — a NFS-e Nacional
            // aceita só 1 cServ por documento (ver DpsBuilder), então
            // serviços de código diferente NUNCA entram na mesma nota;
            // do mesmo código, viram 1 nota com descrição combinada.
            var grupos = linhas.GroupBy(l => l.CodigoTributacaoNacional);

            foreach (var grupo in grupos)
            {
                var primeiraLinha = grupo.First();
                var valorGrupo = Math.Round(grupo.Sum(l => valorPorLinha[l.Id]), 2);
                var composicao = grupo
                    .Select(l => new ItemComposicaoNfse(l.Id, l.ServicoId, l.Descricao, Math.Round(valorPorLinha[l.Id], 2)))
                    .ToList();
                var descricao = grupo.Count() == 1
                    ? primeiraLinha.Descricao
                    : string.Join("; ", grupo.Select(l => l.Descricao).Distinct());

                // Determinística: mesmo Contrato+competência+código
                // tributário nunca gera 2 notas, mesmo se o usuário
                // clicar "Emitir" duas vezes sem perceber — backstop
                // real de idempotência (a checagem de
                // "JaEmitidoNestaCompetencia" na listagem é só UX).
                var idempotencyKey = $"notamensal-{contrato.Id:N}-{request.Competencia:yyyyMM}-{primeiraLinha.CodigoTributacaoNacional}";

                try
                {
                    var resultado = await _emitirNfse.ExecutarAsync(
                        new EmitirNfseRequest(
                            contrato.EmpresaId, contrato.ClienteId, primeiraLinha.ServicoId,
                            valorGrupo, descricao, request.Competencia, contrato.Id, idempotencyKey,
                            Composicao: composicao),
                        cancellationToken);

                    resultados.Add(new ItemResultadoNotaMensalResponse(
                        contrato.Id, clienteNome, descricao, resultado.Sucesso, resultado.NfseId, resultado.NumeroNfse,
                        resultado.Sucesso ? null : (resultado.MensagemErro ?? "Rejeitada pela SEFIN."), contratoAtualizado));
                }
                catch (Exception ex) when (ex is RegraNegocioException or RecursoNaoEncontradoException or NfseValidationException or NfseCertificateException or NfseApiException)
                {
                    // Falhas conhecidas do próprio domínio/integração —
                    // mensagem direta pro usuário.
                    resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, clienteNome, descricao, false, null, null, ex.Message, contratoAtualizado));
                }
                catch (Exception ex)
                {
                    // Qualquer outra coisa (timeout de rede, etc.) —
                    // captura mesmo assim: um item que estoura aqui não
                    // pode derrubar o restante do lote.
                    resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, clienteNome, descricao, false, null, null, $"Falha inesperada: {ex.Message}", contratoAtualizado));
                }
            }
        }

        return resultados;
    }

    /// <summary>
    /// "Gravar no contrato": o valor novo de cada linha fixa passa a ser o do
    /// Contrato, como Alteração no Histórico de valores (não mexe no ciclo do
    /// reajuste), com vigência no 1º dia da competência. Gravado antes de
    /// emitir: a correção do cadastro vale mesmo se a SEFIN recusar a nota.
    /// </summary>
    private async Task GravarValoresNoContratoAsync(Guid contratoId, IReadOnlyList<ValorDecidido> novos, DateOnly competencia, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstAsync(c => c.Id == contratoId, cancellationToken);
        var linhas = await _db.ContratoServicos.Where(cs => cs.ContratoId == contratoId).ToListAsync(cancellationToken);

        var valorAnterior = contrato.ValorAtual;
        foreach (var novo in novos)
        {
            var linha = linhas.First(l => l.Id == novo.ContratoServicoId);
            (linha.Quantidade, linha.ValorUnitario) = RegrasValorNotaMensal.RecomporLinha(linha.Quantidade, novo.Valor);
        }
        contrato.ValorAtual = linhas.Sum(l => l.Quantidade * l.ValorUnitario);

        var vigencia = new DateOnly(competencia.Year, competencia.Month, 1);
        _db.MovimentacoesValorContrato.Add(new MovimentacaoValorContrato
        {
            ContratoId = contrato.Id,
            Tipo = TipoMovimentacaoValor.Alteracao,
            Status = StatusMovimentacaoValor.Aplicado,
            DataVigencia = vigencia,
            ValorAnterior = valorAnterior,
            ValorNovo = contrato.ValorAtual,
            PercentualAplicado = valorAnterior == 0 ? null : Math.Round((contrato.ValorAtual - valorAnterior) / valorAnterior * 100, 4),
            Motivo = $"Valor ajustado na Nota Mensal de {competencia:MM/yyyy}",
            ComposicaoJson = await ComposicaoContrato.SerializarAsync(
                _db, linhas.Select(l => (l.ServicoId, l.Quantidade, l.ValorUnitario)), cancellationToken)
        });

        _auditLogWriter.Registrar("AlterarServicosContrato", "Contrato", contrato.Id,
            new { Origem = "NotaMensal", Competencia = competencia, ValorAnterior = valorAnterior, ValorNovo = contrato.ValorAtual });

        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
    }
}
